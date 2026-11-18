"""读写 wav、估音高（只用 numpy）。"""
import io, wave, numpy as np

def read_wav_bytes(b):
    with wave.open(io.BytesIO(b), "rb") as w:
        sr, n, ch, sw = w.getframerate(), w.getnframes(), w.getnchannels(), w.getsampwidth()
        raw = w.readframes(n)
    assert sw == 2, f"只支持 16 位，收到 {sw * 8} 位"
    x = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
    if ch > 1: x = x.reshape(-1, ch).mean(axis=1)
    return x, sr

def read_wav(path):
    return read_wav_bytes(open(path, "rb").read())

def write_wav(path, x, sr):
    y = np.clip(x, -1, 1); y = (y * 32767.0).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr); w.writeframes(y.tobytes())

def f0_median(x, sr, lo=60, hi=500):
    """自相关法估一段语音的音高中位数（赫兹）。只算有声的帧。"""
    fl = int(sr * 0.04); hop = int(sr * 0.01); out = []
    rms_all = np.sqrt(np.mean(x ** 2) + 1e-12)
    for i in range(0, len(x) - fl, hop):
        f = x[i:i + fl]; f = f - f.mean()
        if np.sqrt(np.mean(f ** 2)) < rms_all * 0.5: continue
        ac = np.correlate(f, f, "full")[fl - 1:]
        if ac[0] <= 0: continue
        a, b = int(sr / hi), int(sr / lo)
        k = a + int(np.argmax(ac[a:b]))
        if ac[k] / ac[0] > 0.45: out.append(sr / k)
    return float(np.median(out)) if out else 0.0
