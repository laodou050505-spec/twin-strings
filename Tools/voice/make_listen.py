"""生成配音试听页 listen.html：最上面是“和上次通过的版本不一样的句子”（新旧对比），下面是全部 72 句。"""
import json, os, html, time, filecmp, numpy as np, audio_util as au, post
lines = json.load(open('../voice_lines.json', encoding='utf-8'))
names = {'weight': '秤砣', 'scarecrow': '稻草人', 'kite': '纸鸢', 'bamboo': '竹竿侠', 'iron': '铁甲将军', 'tumbler': '不倒翁'}
evs = {'pick': '选人', 'win': '赢了', 'lose': '输了', 'champ': '拿金腰带', 'mirror': '最终战（吃惊）', 'rwin': '赢一小局', 'rlose': '输一小局'}
def voiced(path):
    x, sr = au.read_wav(path); fl = int(sr * 0.02); e = np.array([np.sqrt(np.mean(x[i:i + fl] ** 2)) for i in range(0, len(x) - fl, fl)])
    return len(x) / sr, float(np.sum(e > e.max() * 0.06) * 0.02)
changed = []; os.makedirs('out/prev', exist_ok=True)
for l in lines:
    f = l['file']
    if os.path.exists(f'out/prev_raw/{f}.wav') and not filecmp.cmp(f'out/raw/{f}.wav', f'out/prev_raw/{f}.wav', shallow=False):
        y, sr = post.process(f'out/prev_raw/{f}.wav', l['puppet'], f.split('_')[1]); au.write_wav(f'out/prev/{f}.wav', y, sr)
        changed.append((l, voiced(f'out/prev/{f}.wav'), voiced(f'out/game/{f}.wav')))
v = int(time.time()); order = ['pick', 'rwin', 'rlose', 'win', 'lose', 'champ', 'mirror']
fresh = [l for l in lines if not os.path.exists(f"out/pinned/{l['file']}.wav")]      # 还没定稿（没通过试听）的句子：排在最上面
out = ['''<!doctype html><html lang="zh"><head><meta charset="utf-8"><title>双偶 · 配音试听</title>
<style>
:root{--bg:#f6efe2;--ink:#2a2420;--line:#d9cdb8;--red:#c8321e;--soft:#efe5d2}
@media (prefers-color-scheme: dark){:root{--bg:#211d1a;--ink:#f1e9da;--line:#4a4038;--red:#ef6a52;--soft:#2c2622}}
body{background:var(--bg);color:var(--ink);font:16px/1.5 -apple-system,"PingFang SC",sans-serif;margin:0;padding:24px 16px 60px}
main{max-width:980px;margin:0 auto}h1{font-size:26px;margin:0 0 4px}p.tip{margin:0 0 20px;opacity:.75}
h2{font-size:20px;margin:28px 0 8px;padding-bottom:6px;border-bottom:2px solid var(--red)}
table{width:100%;border-collapse:collapse}td,th{padding:8px 6px;border-bottom:1px solid var(--line);text-align:left;vertical-align:middle}
th{font-size:13px;opacity:.7;font-weight:600}td.t{font-size:17px}td.e{white-space:nowrap;font-size:14px;opacity:.8}
audio{height:32px;width:210px}tr.en td.t{font-size:15px}.id{font:12px ui-monospace,monospace;opacity:.5}.n{font-size:12px;opacity:.65}
.box{background:var(--soft);border-radius:10px;padding:4px 14px 12px;margin:14px 0 6px}.wrap{overflow-x:auto}
</style></head><body><main>
<h1>双偶 · 配音试听</h1>''']
if changed:
    out.append(f'<div class="box"><h2>和上次通过的版本不一样的 {len(changed)} 句</h2><p class="tip">左边是现在包里的，右边是上次通过的旧版本。</p><div class="wrap"><table><tr><th>木偶 · 时刻</th><th>台词</th><th>现在包里的</th><th>上次通过的</th></tr>')
    for l, o, n in changed:
        f = l['file']; ev = f.split('_')[1]
        out.append(f'<tr class="{l["lang"]}"><td class="e">{names[l["puppet"]]} · {evs[ev]} · {"中" if l["lang"] == "zh" else "英"}</td><td class="t">{html.escape(l["text"])}<br><span class="id">{f}</span></td>'
                   f'<td><audio controls preload="none" src="out/game/{f}.wav?v={v}"></audio><br><span class="n">出声 {n[1]:.1f} 秒 / 总长 {n[0]:.1f} 秒</span></td>'
                   f'<td><audio controls preload="none" src="out/prev/{f}.wav?v={v}"></audio><br><span class="n">出声 {o[1]:.1f} 秒 / 总长 {o[0]:.1f} 秒</span></td></tr>')
    out.append('</table></div></div>')
if fresh:
    out.append(f'<div class="box"><h2>这次新增、等你试听的 {len(fresh)} 句</h2><p class="tip">三局两胜里每打完一小局说的话：赢一小局 = 来劲、催着再来；输一小局 = 不服气、还要打。</p><div class="wrap"><table><tr><th>木偶 · 时刻</th><th>台词</th><th>游戏里用的</th></tr>')
    for l in sorted(fresh, key=lambda l: (list(names).index(l['puppet']), order.index(l['file'].split('_')[1]), l['lang'] != 'zh', l['file'])):
        f = l['file']; n = voiced(f'out/game/{f}.wav')
        out.append(f'<tr class="{l["lang"]}"><td class="e">{names[l["puppet"]]} · {evs[f.split("_")[1]]} · {"中" if l["lang"] == "zh" else "英"}</td><td class="t">{html.escape(l["text"])}<br><span class="id">{f}</span></td>'
                   f'<td><audio controls preload="none" src="out/game/{f}.wav?v={v}"></audio><br><span class="n">出声 {n[1]:.1f} 秒 / 总长 {n[0]:.1f} 秒</span></td></tr>')
    out.append('</table></div></div>')
out.append(f'<p class="tip">下面是全部 {len(lines)} 句，都是现在包里的版本。哪句不对，把那一行灰色的文件名告诉我。</p>')
rows = {}; ch = {l['file'] for l, _, _ in changed}
for l in lines: rows.setdefault(l['puppet'], []).append((order.index(l['file'].split('_')[1]), l['lang'], l['file'], l['file'].split('_')[1], l['text']))
for kit in names:
    out.append(f'<h2>{names[kit]}</h2><div class="wrap"><table><tr><th>时刻</th><th>台词</th><th>游戏里用的</th></tr>')
    for _, lang, f, ev, text in sorted(rows[kit], key=lambda r: (r[0], r[1] != 'zh', r[2])):
        out.append(f'<tr class="{lang}"><td class="e">{evs[ev]} · {"中" if lang == "zh" else "英"}{" · 换过" if f in ch else ""}{" · 新增" if not os.path.exists(f"out/pinned/{f}.wav") else ""}</td><td class="t">{html.escape(text)}<br><span class="id">{f}</span></td><td><audio controls preload="none" src="out/game/{f}.wav?v={v}"></audio></td></tr>')
    out.append('</table></div>')
out.append('</main></body></html>')
open('listen.html', 'w', encoding='utf-8').write('\n'.join(out)); print('试听页已更新，和上次通过版本不同的', len(changed), '句')
