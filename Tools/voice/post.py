"""第三步：后期。把合成出来的“干净人声”处理成“从这只木偶身体里发出来的声音”，再放进游戏的 Resources/Voice/。
每只木偶一条处理链（只用 numpy）：变调 → 身体共鸣（均衡 / 梳状滤波）→ 材质层（抖动、颤音、摩擦噪声）→ 小戏台的近距离反射 → 轻微过载 → 统一响度。
用法：python post.py [只处理的文件名前缀]"""
import json, os, sys, numpy as np, audio_util as au

OUT = os.path.expanduser("~/Documents/ChatGPT/StringBrawl/Assets/_Brawl/Resources/Voice")
rng = np.random.default_rng(7)

def trim(x, sr, keep=0.32):
    """掐头去尾，但尾巴要留够：人说完一句话，声音是慢慢落下去的。阈值压到 -52 dB（很轻的尾音也算），
    最后一个听得见的音之后再留 keep 秒，然后用 0.18 秒的余弦曲线淡出——不是一刀切掉。"""
    pk = np.max(np.abs(x)) + 1e-9; idx = np.where(np.abs(x) > pk * 10 ** (-52 / 20))[0]
    if len(idx) == 0: return x
    a = max(0, idx[0] - int(sr * 0.02)); b = idx[-1] + int(sr * keep)
    y = x[a:min(len(x), b)].copy()
    if b > len(x): y = np.concatenate([y, np.zeros(b - len(x), np.float32)])        # 原文件尾巴不够长：补上安静的一段，给后面的余音留地方
    fi, fo = int(sr * 0.005), int(sr * 0.18)
    y[:fi] *= np.linspace(0, 1, fi); y[-fo:] *= 0.5 * (1 + np.cos(np.linspace(0, np.pi, fo)))
    return y.astype(np.float32)

def soften_cut(x, sr):
    """合成出来就被截断的句子（文件最后还很响）：先把最后 0.12 秒淡出，免得“咔”一下断掉。"""
    pk = np.max(np.abs(x)) + 1e-9; n = int(sr * 0.03)
    if 20 * np.log10(np.sqrt(np.mean(x[-n:] ** 2)) / pk + 1e-9) > -32:
        fo = int(sr * 0.12); x = x.copy(); x[-fo:] *= 0.5 * (1 + np.cos(np.linspace(0, np.pi, fo)))
    return x

_ir = {}
def room_tail(x, sr, dur=0.28, mix=0.085):
    """小戏台的余音：一小段很快衰减的混响（0.28 秒），接在每句话后面——句尾有一点点拖音，不是戛然而止。"""
    if sr not in _ir:
        n = int(sr * dur); g = np.random.default_rng(3).standard_normal(n).astype(np.float32) * np.exp(-np.arange(n) / (sr * 0.07))
        g[:int(sr * 0.012)] = 0; G = np.fft.rfft(g); f = np.fft.rfftfreq(n, 1 / sr); f[0] = 1e-3
        g = np.fft.irfft(G * hp(f, 250) * lp(f, 4200), n); _ir[sr] = (g / np.sqrt(np.sum(g ** 2))).astype(np.float32)
    ir = _ir[sr]; n = len(x) + len(ir); wet = np.fft.irfft(np.fft.rfft(x, n) * np.fft.rfft(ir, n), n)
    y = np.zeros(n, np.float32); y[:len(x)] = x
    return y + wet.astype(np.float32) * mix

def resample(x, r):
    """整体变调：r>1 更尖更快（小个子），r<1 更沉更慢（大块头）。音高和共振峰一起变，听起来是“换了个身体”。"""
    n = int(len(x) / r); return np.interp(np.arange(n) * r, np.arange(len(x)), x).astype(np.float32)

def eq(x, sr, fn):
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / sr); f[0] = 1e-3
    return np.fft.irfft(X * fn(f), len(x)).astype(np.float32)

def bell(f, f0, q, db):
    g = 10 ** (db / 20); return 1 + (g - 1) / (1 + ((f / f0 - f0 / f) * q) ** 2)
def hp(f, fc, n=2): return 1 / np.sqrt(1 + (fc / f) ** (2 * n))
def lp(f, fc, n=2): return 1 / np.sqrt(1 + (f / fc) ** (2 * n))

def comb(x, sr, ms, fb, mix):
    """梳状滤波：声音在一个小空腔里来回弹（竹筒、头盔、铁罐）的那种“嗡”。"""
    d = max(1, int(sr * ms / 1000)); y = np.concatenate([x, np.zeros(d * 12, np.float32)]).copy()
    for k in range(d, len(y), d):
        e = min(len(y), k + d); y[k:e] += fb * y[k - d:k - d + (e - k)]
    y = y[:len(x) + d * 6]; dry = np.concatenate([x, np.zeros(len(y) - len(x), np.float32)])
    return dry * (1 - mix) + y * mix

def reflections(x, sr, taps):
    n = len(x) + int(sr * 0.05); y = np.zeros(n, np.float32); y[:len(x)] = x
    for ms, g in taps:
        d = int(sr * ms / 1000); y[d:d + len(x)] += g * x
    return y

def tremolo(x, sr, rate, depth):
    t = np.arange(len(x)) / sr; return x * (1 - depth * 0.5 * (1 + np.sin(2 * np.pi * rate * t)))

def vibrato(x, sr, rate, ratio):
    t = np.arange(len(x)); dev = ratio / (2 * np.pi * rate / sr) * np.sin(2 * np.pi * rate * t / sr)
    return np.interp(t + dev, t, x).astype(np.float32)

def texture(x, sr, lo, hi, mix):
    """材质层：一层只在说话时出现的摩擦噪声（稻草、纸），跟着音量起伏。"""
    env = np.abs(x); k = int(sr * 0.02); env = np.convolve(env, np.ones(k) / k, "same")
    nz = rng.standard_normal(len(x)).astype(np.float32)
    nz = eq(nz, sr, lambda f: hp(f, lo, 3) * lp(f, hi, 3))
    return x + nz * env * mix * 6

def sat(x, g): return np.tanh(x * g) / np.tanh(g)

def loud(x, rms_db=-16.0, peak=0.92):
    act = x[np.abs(x) > np.max(np.abs(x)) * 0.05]; r = np.sqrt(np.mean(act ** 2) + 1e-12)
    y = x * (10 ** (rms_db / 20) / r); pk = np.max(np.abs(y))
    return y * (peak / pk) if pk > peak else y

# 第二版：用户反馈“还是假、像机器人”。梳状滤波、快速抖动、颤音这几样本身就是做机器人嗓的手法，全部拿掉。
# 现在每只只做三件事：很小幅度的变调（换个身体的大小）、一两处温和的共鸣（材质）、极轻的摩擦层；人声本身的表演不动。
# 第四版：用户要求“配音里不能有别的声音”。所有往里加东西的处理都拿掉了——余响、反射声、摩擦噪声、过载、变调、共鸣均衡一概不用。
# 现在只做三件事：掐头去尾（尾巴留够、慢慢淡出）、滤掉 70 赫兹以下听不见的低频隆隆声、把响度调到位。出来的就是这位配音自己的声音。
# 每个时刻的响度不一样：夺冠最响，输了最轻（统一拉平会把语气抹掉）
EVENT_DB = {"pick": -16.5, "win": -15.5, "champ": -15.0, "lose": -19.0, "mirror": -17.5, "rwin": -15.8, "rlose": -16.5}

def process(path, kit, ev):
    x, sr = au.read_wav(path); x = trim(soften_cut(x, sr), sr, keep=0.22)
    y = eq(x, sr, lambda f: hp(f, 70))
    y = loud(trim(y, sr, keep=0.2), rms_db=EVENT_DB.get(ev, -16.0))
    return y, sr

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True); prefix = sys.argv[1] if len(sys.argv) > 1 else None
    lines = json.load(open("../voice_lines.json", encoding="utf-8")); n = 0
    for ln in lines:
        if prefix and not ln["file"].startswith(prefix): continue
        src = f"out/raw/{ln['file']}.wav"
        if not os.path.exists(src): src = f"out/pinned/{ln['file']}.wav"     # 定稿后 out/raw 不保留：直接读定稿原声
        y, sr = process(src, ln["puppet"], ln["file"].split("_")[1])
        au.write_wav(os.path.join(OUT, ln["file"] + ".wav"), y, sr); n += 1
        if n % 12 == 1: print(f"{ln['file']:24s} {len(y) / sr:4.2f} 秒 峰值 {np.max(np.abs(y)):.2f} 有效响度 {20 * np.log10(np.sqrt(np.mean(y ** 2)) + 1e-9):.1f} dB")
    import shutil
    os.makedirs("out/game", exist_ok=True)
    for ln in lines: shutil.copy(os.path.join(OUT, ln["file"] + ".wav"), f"out/game/{ln['file']}.wav")     # 试听页用的那一份同步更新
    print("处理完成", n, "条 →", OUT)
