"""BreezeBlue 接口的最小客户端（只用标准库）。密钥从 ~/.config/breezeblue/key.txt 读，绝不打印。
接口地址、认证头、路径都来自官方 Python 开发包 breeze-blue 0.23.0 的源码。"""
import json, os, urllib.request, urllib.parse, urllib.error

BASE = "https://api.breeze.blue"
PROXY = "http://127.0.0.1:7897"

def _key():
    return open(os.path.expanduser("~/.config/breezeblue/key.txt"), encoding="utf-8").read().strip()

import ssl
_ctx = ssl.create_default_context(cafile="/etc/ssl/cert.pem")   # 用系统的根证书校验（Blender 自带的 Python 找不到证书库），不关闭校验
_opener = urllib.request.build_opener(urllib.request.ProxyHandler({"http": PROXY, "https": PROXY}), urllib.request.HTTPSHandler(context=_ctx))

def call(method, path, params=None, body=None, timeout=120, raw=False):
    url = BASE + path
    if params:
        q = {k: v for k, v in params.items() if v is not None}
        if q: url += "?" + urllib.parse.urlencode(q, doseq=True)
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("xi-api-key", _key()); req.add_header("user-agent", "breeze-client/1.0")
    if data is not None: req.add_header("content-type", "application/json")
    try:
        with _opener.open(req, timeout=timeout) as r:
            b = r.read(); ct = r.headers.get("content-type", "")
            if raw: return b, ct, dict(r.headers)
            return json.loads(b.decode("utf-8")) if b else {}
    except urllib.error.HTTPError as e:
        msg = e.read().decode("utf-8", "replace")[:600]
        raise RuntimeError(f"HTTP {e.code} {path}: {msg}") from None
