"""第二步：把选中的候选保存成正式音色，再按台词清单逐句合成（原始文件放 out/raw/）。
用法：python gen_tts.py save | tts [只重做的文件名前缀]"""
import json, os, sys, time, bb, audio_util as au
from voices_cfg import PUPPETS

# 每只木偶选中的候选（按估出的音高选最贴近设定的；铁甲将军用第二轮的）
CHOSEN = {"weight": ("weight", 2), "scarecrow": ("scarecrow", 2), "kite": ("kite", 3), "bamboo": ("bamboo", 2), "iron": ("iron_b", 1), "tumbler": ("tumbler", 3)}
# 角色底色（这只木偶平时怎么说话）
BASE = {
    "zh": {"weight": "一只铁秤砣木偶：嗓音低沉粗哑，话少，闷声闷气。", "scarecrow": "一只稻草人木偶：嗓子沙哑，毛毛躁躁，不服输。",
           "kite": "一只纸鸢木偶：声音清脆明亮，语速快，爱逗人、有点欠。", "bamboo": "一位竹竿侠客木偶：斯文克制，彬彬有礼，不紧不慢。",
           "iron": "一位铁甲老将军木偶：嗓门大，威严，中气十足。", "tumbler": "一只不倒翁木偶：圆滚滚、憨憨的小孩，爱笑。"},
    "en": {"weight": "A heavy iron puppet: deep, gruff, few words.", "scarecrow": "A scarecrow puppet: hoarse, scrappy, never gives up.",
           "kite": "A paper-kite puppet: bright, quick, cheeky and teasing.", "bamboo": "A bamboo swordsman puppet: refined, courteous, unhurried.",
           "iron": "An old armored general puppet: loud, commanding, full-chested.", "tumbler": "A roly-poly puppet: a chubby, silly, giggly kid."},
}
# 每个时刻一种语气：选人 / 赢 / 输 / 夺冠 / 吃惊，要一听就分得出来
EVENT = {
    "zh": {"pick": "【此刻】抢着上前请战。【语气】跃跃欲试、兴奋，语调上扬，干脆有力。",
           "win": "【此刻】刚刚打赢。【语气】得意洋洋，带着笑，声音响亮高昂，尾音往上扬。",
           "lose": "【此刻】刚被打倒在地。【语气】泄气、虚弱、叹着气，声音低而慢，越说越小声，有气无力。",
           "champ": "【此刻】高举金腰带。【语气】狂喜、激动得放声高喊，是最兴奋、最响亮的一句。",
           "mirror": "【此刻】突然看见对面站着一个和自己一模一样的。【语气】猛地吃了一惊，倒吸一口气，难以置信，迟疑着说出来。",
           "rwin": "【此刻】三局两胜，刚赢下一局，比赛还没完。【语气】来劲了，自信，催对手再来，干脆有力。",
           "rlose": "【此刻】三局两胜，刚输掉一局，比赛还没完。【语气】不服气，嘴硬，爬起来还要再打，有劲，不泄气。"},
    "en": {"pick": "[Moment] Stepping forward to volunteer for a fight. [Tone] Eager and excited, rising pitch, short and punchy.",
           "win": "[Moment] Just won the fight. [Tone] Smug and laughing, loud and high, ending on a rising note.",
           "lose": "[Moment] Just knocked flat on the ground. [Tone] Deflated, weak, sighing; low, slow and trailing off, barely any breath left.",
           "champ": "[Moment] Holding up the champion's belt. [Tone] Ecstatic, shouting with joy; the loudest, most excited line of all.",
           "mirror": "[Moment] Suddenly sees an exact double of oneself. [Tone] A sharp gasp of shock, disbelief, hesitant.",
           "rwin": "[Moment] Best of three; just took one round, the match is not over. [Tone] Fired up, confident, daring the opponent to come again; short and punchy.",
           "rlose": "[Moment] Best of three; just dropped one round, the match is not over. [Tone] Defiant, stubborn, getting back up to fight on; energetic, not deflated."},
}
TAIL = {"zh": "像真人演员在给动画里的木偶配音，表演要夸张、有血有肉；不要像播音员、朗读者或语音助手。",
        "en": "Like a real voice actor performing a puppet in a cartoon: exaggerated and alive. Never sound like a narrator, a reader or an assistant."}
SPEED = {"weight": 0.95, "kite": 1.1}
ONLY_EV = set(filter(None, os.environ.get("SB_EVENTS", "").split(",")))   # 只合成这些时刻的台词（例如 SB_EVENTS=rwin,rlose）；不设 = 全部
TAKES = int(os.environ.get("SB_TAKES", "3"))   # 每句出几个版本（个别句子没有合格的，可以单独加到 6 个）

def save():
    voices = json.load(open("out/voices.json")) if os.path.exists("out/voices.json") else {}
    for kit, (src, n) in CHOSEN.items():
        if kit in voices: print(kit, "已保存", voices[kit]); continue
        meta = json.load(open(f"out/design/{src}.json"))[n - 1]; c = PUPPETS[kit]
        r = bb.call("POST", f"/v1/voice-previews/{meta['id']}/save", body={
            "voice_name": "双偶·" + c["zh"], "voice_description": "《双偶 Twin Strings》木偶“" + c["zh"] + "”的台词嗓音。",
            "language_code": "zh", "gender": c["gender"], "age": c["age"], "tone": c["tone"], "tags": ["twin-strings"]})
        voices[kit] = r.get("voice_id"); print(c["zh"], "→", voices[kit])
        json.dump(voices, open("out/voices.json", "w"), ensure_ascii=False, indent=1)

TAKE_DIR = "out/takes_b"   # 第三版：同一只木偶必须是同一个人的嗓子

def tts(prefix=None):
    """每句合成 TAKES 个版本。开“贴近原音色”（use_speaker_boost），表演指令的强度用最低一档（不传 guidance = 1）：
    语气主要靠台词里的语气词和标点带出来，嗓子不跑。两路并发（账户上限 2）。"""
    from concurrent.futures import ThreadPoolExecutor
    voices = json.load(open("out/voices.json")); os.makedirs(TAKE_DIR, exist_ok=True)
    lines = json.load(open("../voice_lines.json", encoding="utf-8")); jobs = []
    for ln in lines:
        name = ln["file"]; kit = ln["puppet"]; ev = name.split("_")[1]; lang = ln["lang"]
        if prefix and not name.startswith(prefix): continue
        if ONLY_EV and ev not in ONLY_EV: continue
        body = {"text": ln["text"], "model_id": "breeze-tts-2", "language_code": lang,
                "instructions": EVENT[lang][ev], "voice_settings": {"use_speaker_boost": True, "speed": SPEED.get(kit, 1.0)}}
        for k in range(1, TAKES + 1):
            path = f"{TAKE_DIR}/{name}_t{k}.wav"
            if not os.path.exists(path): jobs.append((name, k, voices[kit], body, path))
    def run(j):
        name, k, vid, body, path = j
        for attempt in range(3):
            try:
                raw, ct, _ = bb.call("POST", f"/v1/text-to-speech/{vid}", params={"output_format": "wav_24000"}, body=body, raw=True, timeout=180)
                x, sr = au.read_wav_bytes(raw)
                if 0.35 <= len(x) / sr <= 7.0 or attempt == 2: open(path, "wb").write(raw); return
            except Exception as e:
                print(name, k, "失败", str(e)[:160], flush=True); time.sleep(4)
    with ThreadPoolExecutor(2) as ex: list(ex.map(run, jobs))
    print("合成完成", len(jobs), "个版本", flush=True)

# “输了”那一句：用户反馈像在平平地念。只对这一句把表演强度调上去（guidance 3），多出几个版本，由 pick_takes 挑“嗓子没跑、但真的在泄气”的
LOSE = {"zh": "【此刻】刚被打倒在地，输了。【语气】非常沮丧、委屈、不甘心，像泄了气一样；先短短叹一口气，语气低落，尾音往下掉。语速正常，干脆说完，不要拖长音，也不要平铺直叙地念。",
        "en": "[Moment] Just knocked flat and lost. [Tone] Crushed, sulky, bitter, like the air going out; a short sigh first, downcast, the ending drooping. Normal pace, said in one go, never drawn out, and never read flatly."}
def tts_lose(n=6):
    from concurrent.futures import ThreadPoolExecutor
    voices = json.load(open("out/voices.json")); lines = json.load(open("../voice_lines.json", encoding="utf-8")); jobs = []
    for ln in lines:
        name = ln["file"]; kit = ln["puppet"]; lang = ln["lang"]
        if name.split("_")[1] != "lose": continue
        body = {"text": ln["text"], "model_id": "breeze-tts-2", "language_code": lang, "instructions": BASE[lang][kit] + LOSE[lang],
                "voice_settings": {"guidance_scale": 3, "speed": SPEED.get(kit, 1.0) * 0.92}}
        for k in range(1, n + 1):
            path = f"{TAKE_DIR}/{name}_L{k}.wav"
            if not os.path.exists(path): jobs.append((name, k, voices[kit], body, path))
    def run(j):
        name, k, vid, body, path = j
        for attempt in range(3):
            try:
                raw, ct, _ = bb.call("POST", f"/v1/text-to-speech/{vid}", params={"output_format": "wav_24000"}, body=body, raw=True, timeout=180)
                x, sr = au.read_wav_bytes(raw)
                if 0.35 <= len(x) / sr <= 9.0 or attempt == 2: open(path, "wb").write(raw); return
            except Exception as e:
                print(name, k, "失败", str(e)[:160], flush=True); time.sleep(4)
    with ThreadPoolExecutor(2) as ex: list(ex.map(run, jobs))
    print("输了的版本合成完成", len(jobs), flush=True)

# 有情绪的那几个时刻（赢 / 夺冠 / 最终战吃惊）：用户反馈“赢了也很平静”。和“输”一样，把表演强度调上去多出几个版本，嗓子跑没跑由 pick_takes 把关
STRONG = {
    "win": {"zh": "【此刻】刚刚打赢了！【语气】非常兴奋、得意，带着笑喊出来，声音响亮、语速快、音调上扬，中气十足，干脆利落不拖音。绝对不要平静地念。",
            "en": "[Moment] Just won the fight! [Tone] Thrilled and smug, shouted with a laugh; loud, quick, rising, full of energy. Never read it calmly."},
    "champ": {"zh": "【此刻】高举金腰带，全场都在欢呼。【语气】狂喜，激动得大声喊出来，是最响、最亮、最兴奋的一句。语速正常偏快，干脆利落，不要拖长音，也不要平静地念。",
              "en": "[Moment] Holding up the gold belt while the crowd roars. [Tone] Ecstatic, shouting at the top of the voice; the loudest, brightest line. Brisk and crisp, never drawn out, never calm."},
    "mirror": {"zh": "【此刻】突然看见对面站着一个和自己一模一样的。【语气】大吃一惊，声音一下子拔高，难以置信。语速正常，不要拖长音。只要说话声，不要加喘气声。",
               "en": "[Moment] Suddenly sees an exact double of oneself. [Tone] Startled, the voice jumps up, disbelieving. Normal pace, not drawn out. Speech only, no gasping sounds."},
    # 每小局（三局两胜里整场还没完的那一局）：赢 = 来劲、挑衅着催对手再来；输 = 不服气、嘴硬（和整场输了的“泄气”要分得开）
    "rwin": {"zh": "【此刻】三局两胜，刚赢下一局，比赛还没完。【语气】来劲了，自信又有点得意，冲着对手喊“再来”，声音响亮、干脆利落，不拖音。不要平静地念。",
             "en": "[Moment] Best of three; just took one round, the match is not over. [Tone] Fired up, cocky, daring the opponent to come again; loud, crisp, not drawn out. Never read it calmly."},
    "rlose": {"zh": "【此刻】三局两胜，刚输掉一局，比赛还没完。【语气】不服气、嘴硬，咬着牙爬起来还要再打，有劲头、有火气，干脆利落，不拖音。不是泄气，也不要平静地念。",
              "en": "[Moment] Best of three; just dropped one round, the match is not over. [Tone] Defiant, stubborn, gritting teeth and getting back up; fiery and crisp, not drawn out. Not deflated, never calm."},
}
def tts_strong(n=5, only=None, tag="S"):
    from concurrent.futures import ThreadPoolExecutor
    voices = json.load(open("out/voices.json")); lines = json.load(open("../voice_lines.json", encoding="utf-8")); jobs = []
    for ln in lines:
        name = ln["file"]; kit = ln["puppet"]; lang = ln["lang"]; ev = name.split("_")[1]
        if only is None and ev not in STRONG and ev != "lose": continue
        if ONLY_EV and ev not in ONLY_EV: continue
        if only is not None and name not in only: continue
        whole = "把这句台词一字不漏地完整念出来。" if lang == "zh" else " Say every word of the line in full."
        body = {"text": ln["text"], "model_id": "breeze-tts-2", "language_code": lang, "instructions": BASE[lang][kit] + (LOSE[lang] if ev == "lose" else STRONG.get(ev, STRONG["win"])[lang]) + whole,
                "voice_settings": {"guidance_scale": 3, "speed": SPEED.get(kit, 1.0)}}
        for k in range(1, n + 1):
            path = f"{TAKE_DIR}/{name}_{tag}{k}.wav"
            if not os.path.exists(path): jobs.append((name, k, voices[kit], body, path))
    def run(j):
        name, k, vid, body, path = j
        for attempt in range(3):
            try:
                raw, ct, _ = bb.call("POST", f"/v1/text-to-speech/{vid}", params={"output_format": "wav_24000"}, body=body, raw=True, timeout=180)
                x, sr = au.read_wav_bytes(raw)
                if 0.35 <= len(x) / sr <= 9.0 or attempt == 2: open(path, "wb").write(raw); return
            except Exception as e:
                print(name, k, "失败", str(e)[:160], flush=True); time.sleep(4)
    with ThreadPoolExecutor(2) as ex: list(ex.map(run, jobs))
    print("加强版合成完成", len(jobs), flush=True)

if __name__ == "__main__":
    {"save": save, "tts": lambda: tts(sys.argv[2] if len(sys.argv) > 2 else None), "lose": tts_lose, "strong": tts_strong, "more": lambda: tts_strong(10, set(open("out/calm.txt").read().split())), "redo": lambda: tts_strong(int(os.environ.get("SB_N", "8")), set(open("out/redo.txt").read().split()), os.environ.get("SB_TAG", "Q")),
     "brisk": lambda: tts_strong(int(os.environ.get("SB_N", "5")), set(open("out/calm.txt").read().split()), "R")}[sys.argv[1]]()
