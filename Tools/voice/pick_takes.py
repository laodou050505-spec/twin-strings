"""从每句的所有版本里挑一个，拷到 out/raw/。听不到声音，所以按量得出来的东西挑，顺序是：
  1. 干净：句尾没被截断；说话的间隙里底噪低（没有别的声音垫在下面）；
  2. 是“这只木偶的嗓子”：音高中位数离这只木偶的中位数不超过 3 个半音（输的那句放宽到 3.5），音色轮廓离中心近；
  3. 语气：赢 / 夺冠挑响一点、起伏大一点的；
     输——不能平平地念：挑音高起伏大、说得慢、尾音往下掉的（起伏是这句话内部的起伏，不是整句换一个音高）。"""
import glob, json, os, shutil, numpy as np, audio_util as au
from gen_tts import TAKE_DIR
HIGH = {"kite", "tumbler"}
BANDS = np.geomspace(120, 7000, 25)

def feats(path, kit):
    x, sr = au.read_wav(path); pk = np.max(np.abs(x)) + 1e-9
    # 底噪：整个文件里最安静的 30 毫秒有多响（开头结尾总有一小段不说话的地方；那里不安静，就是有别的声音垫着）
    nq = int(sr * 0.03); quiet = min(np.sqrt(np.mean(x[i:i + nq] ** 2)) for i in range(0, max(1, len(x) - nq), nq)); noise_db = 20 * np.log10(quiet / pk + 1e-9)
    tail_db = 20 * np.log10(np.sqrt(np.mean(x[-int(sr * 0.03):] ** 2)) / pk + 1e-9)
    idx = np.where(np.abs(x) > pk * 0.03)[0]; x = x[idx[0]:idx[-1] + 1] if len(idx) else x
    lo, hi = (150, 600) if kit in HIGH else (60, 400)
    fl = int(sr * 0.05); hop = int(sr * 0.01); f0s = []; spec = []; en = []
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / sr); X[f > 1100] = 0; y = np.fft.irfft(X, len(x)); rms_all = np.sqrt(np.mean(y ** 2) + 1e-12)
    win = np.hanning(fl); ff = np.fft.rfftfreq(fl, 1 / sr)
    for i in range(0, len(y) - fl, hop):
        en.append(np.sqrt(np.mean(x[i:i + fl] ** 2)))
        fr = y[i:i + fl]; fr = fr - fr.mean()
        if np.sqrt(np.mean(fr ** 2)) < rms_all * 0.4: continue
        P = np.abs(np.fft.rfft(x[i:i + fl] * win)) ** 2
        spec.append([10 * np.log10(P[(ff >= BANDS[b]) & (ff < BANDS[b + 1])].mean() + 1e-12) for b in range(len(BANDS) - 1)])
        ac = np.correlate(fr, fr, "full")[fl - 1:]
        if ac[0] <= 0: continue
        ac = ac / ac[0]; a, b2 = int(sr / hi), min(int(sr / lo), fl - 1); k = a + int(np.argmax(ac[a:b2]))
        if ac[k] > 0.5: f0s.append(sr / k)
    if len(f0s) < 6 or len(spec) < 3: return None
    st = 12 * np.log2(np.array(f0s) / 100.0); sv = np.mean(np.array(spec), axis=0); sv = sv - sv.mean(); n3 = max(2, len(st) // 3); en = np.array(en)
    return dict(med=float(np.median(st)), spread=float(np.percentile(st, 90) - np.percentile(st, 10)), fall=float(np.median(st[:n3]) - np.median(st[-n3:])),
                rms=float(20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-9)), dur=len(x) / sr, tail=tail_db, sv=sv,
                noise=float(noise_db), voiced=float(np.sum(en > np.max(en) * 0.06) * hop / sr), pause=float(np.mean(en < np.max(en) * 0.08)))

lines = json.load(open("../voice_lines.json", encoding="utf-8")); os.makedirs("out/raw", exist_ok=True)
open("out/fallback.txt", "w").close(); open("out/calm.txt", "w").close(); open("out/redo.txt", "w").close()
names = {"weight": "秤砣", "scarecrow": "稻草人", "kite": "纸鸢", "bamboo": "竹竿侠", "iron": "铁甲将军", "tumbler": "不倒翁"}
for kit in names:
    fs = {}
    for ln in [l for l in lines if l["puppet"] == kit]:
        for p in sorted(glob.glob(f"{TAKE_DIR}/{ln['file']}_*.wav")):
            f = feats(p, kit)
            if f: f["path"] = p; f["strong"] = any(t in os.path.basename(p) for t in ("_L", "_S", "_R", "_Q", "_U")); fs.setdefault(ln["file"], []).append(f)
    base = [v for vs in fs.values() for v in vs if not v["strong"]]                    # 这只木偶“本来的嗓子”：按普通版本算
    for p in sorted(glob.glob(f"out/pinned/{kit}_pick_*.wav")):                       # 再加上用户通过的“选人”定稿：新补的台词必须和已通过的嗓子是同一个人
        f = feats(p, kit)
        if f: f["path"] = p; f["strong"] = False; base.append(f)
    med = float(np.median([v["med"] for v in base])); cen = np.median(np.array([v["sv"] for v in base]), axis=0)
    for vs in list(fs.values()) + [base]:
        for v in vs: v["dp"] = v["med"] - med; v["ds"] = float(np.sqrt(np.mean((v["sv"] - cen) ** 2)))
    ratios = []; vr = []
    dsm = float(np.median([v["ds"] for v in base])); rmsm = float(np.median([v["rms"] for v in base])); chosen = []; fb = 0; lose = []
    for ln in [l for l in lines if l["puppet"] == kit]:
        ev = ln["file"].split("_")[1]; cand = fs.get(ln["file"], [])
        if not cand:
            if not os.path.exists(f"out/pinned/{ln['file']}.wav"): print("没有可用版本", ln["file"])   # 已定稿的句子没有候选是正常的（候选已清理）
            continue
        durm = float(np.median([v["dur"] for v in cand if not v["strong"]] or [v["dur"] for v in cand]))
        vm = float(np.percentile([v["voiced"] for v in cand], 60)); full = lambda v: v["voiced"] >= vm * 0.75      # 念完整了：出声的时间不比这句通常的少太多（基准取偏上的位置，不被漏字的版本拉低）
        # 语气分：只用靠得住的量——整句的音高在“这只木偶的范围内”偏高还是偏低、响度、时长。（句内起伏量不准，不用）
        def emo(v):
            r = v["rms"] - rmsm
            if ev == "lose": return -0.4 * v["dp"] - 0.1 * r - 1.5 * abs(v["dur"] / durm - 1.1)     # 泄气：偏低、轻；时长和平常差不多（不拖）
            if ev in ("win", "champ"): return 0.5 * v["dp"] + 0.3 * r                              # 兴奋：偏高、响
            if ev in ("rwin", "rlose"): return 0.25 * v["dp"] + 0.3 * r                            # 小局：有劲、响，但不用像整场赢了那么高
            if ev == "mirror": return 0.3 * v["dp"]
            return 0.3 * r
        lim = 3.5 if ev in ("lose", "win", "champ", "mirror", "rwin", "rlose") else 3.0
        good = lambda v, k: full(v) and v["tail"] < -32 and v["noise"] < -45 and abs(v["dp"]) <= lim and v["ds"] <= dsm * k and v["dur"] <= durm * 1.3   # 平静版也不许拖
        # 有情绪的时刻优先用加强表演的版本。人一激动，嗓门本来就会高一点、泄气时会低一点——所以加强版的音高放宽：
        # 赢 / 夺冠 / 吃惊 允许比平时高到 5 个半音、低到 3 个；输 允许低到 5 个、高到 3 个。再多就是换了个人
        up, dn = (3.0, 5.0) if ev == "lose" else (5.0, 3.0)
        cap = max(durm * 1.25, durm + 0.4)   # 很短的句子（一秒上下）按比例卡太死：至少给 0.4 秒余量；原注→                                                                      # 不许拖：比这句平常念的时长最多长四分之一
        strong_ok = [v for v in cand if v["strong"] and full(v) and v["dur"] <= cap and v["tail"] < -32 and v["noise"] < -45 and -dn <= v["dp"] <= up and v["ds"] <= dsm * 1.7]
        ok = strong_ok or [v for v in cand if not v["strong"] and good(v, 1.35)]
        if ok: v = max(ok, key=emo)
        else:
            fb += 1; open("out/fallback.txt", "a").write(ln["file"] + "\n")
            v = min(cand, key=lambda q: abs(q["dp"]) / 3 + q["ds"] / max(dsm, 1e-6) + (2 if q["tail"] >= -32 else 0) + (1 if q["noise"] >= -45 else 0) + 3 * max(0.0, q["dur"] / durm - 1.3) + (4 if not full(q) else 0))
        shutil.copy(v["path"], f"out/raw/{ln['file']}.wav"); chosen.append(v)
        if ev != "pick": lose.append(v)
        ratios.append(v["dur"] / durm); vr.append(v["voiced"] / vm)
        if ev != "pick" and not v["strong"]: open("out/calm.txt", "a").write(ln["file"] + "\n")
        if not full(v) or abs(v["dp"]) > 5.2 or v["dur"] > durm * 1.3: open("out/redo.txt", "a").write(ln["file"] + "\n")     # 还不合格：漏字 / 嗓子跑了 / 拖长
    dp = np.array([v["dp"] for v in chosen])
    print(f"{names[kit]}：音高跨度 {dp.max() - dp.min():.1f} 半音（最大偏离 {np.abs(dp).max():.1f}）；底噪最差 {max(v['noise'] for v in chosen):.0f} dB；没有合格版本 {fb} 句；有情绪的 8 句里用了加强版 {sum(v['strong'] for v in lose)} 句；时长是平常念法的 {min(ratios):.2f}–{max(ratios):.2f} 倍；出声时间最少的一句是通常的 {min(vr):.2f} 倍")

# 定稿的句子（用户试听通过的版本）放在 out/pinned/：不管上面怎么挑，最后都用定稿的。想重做某一句，先把它从 out/pinned/ 里删掉
import glob as _g
for _p in _g.glob("out/pinned/*.wav"): shutil.copy(_p, "out/raw/" + os.path.basename(_p))
print("已套用定稿", len(_g.glob("out/pinned/*.wav")), "句")
