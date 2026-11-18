using System.Collections.Generic;
using UnityEngine;

namespace StringBrawl
{
    /// <summary>木偶的台词（中英文各一套）。什么时候说：
    ///   pick   选人时被选中           win    整场打赢了            lose   整场打输了
    ///   champ  拿到金腰带             mirror 闯关最后一战，发现对面是和自己一模一样的木偶
    ///   rwin   三局两胜里赢下一小局（整场还没完）：还有余力、催着再来      rlose  输掉一小局：不服气、爬起来再打
    /// 每句都很短（木偶戏的念白，一口气说完）。配音文件放在 Resources/Voice/ 下，名字是「木偶_时刻_语言_第几句」，
    /// 例如 weight_pick_zh_1、kite_win_en_1；没有配音文件时只出字幕纸条，不报错。</summary>
    public static class SBLines
    {
        static readonly Dictionary<string, (string[] zh, string[] en)> all = new()
        {
            // 每个时刻一种语气：选人 = 跃跃欲试；赢 = 得意、带笑；输 = 泄气、带叹；夺冠 = 狂喜。句子里的语气词（哼、哈哈、唉、哎哟）就是给配音定调的
            // 秤砣：闷、话少
            ["weight.pick"] = (new[] { "我来。", "交给我。" }, new[] { "Leave it to me.", "I'll handle it." }),
            ["weight.win"] = (new[] { "哼，分量够了吧。" }, new[] { "Hmph. Heavy enough?" }),
            ["weight.lose"] = (new[] { "唉……砸空了。" }, new[] { "Ugh... I missed." }),
            ["weight.champ"] = (new[] { "嗬！金腰带，压得住！" }, new[] { "Hah! I can carry this!" }),
            ["weight.rwin"] = (new[] { "再来，我还没使劲。", "就这点分量？" }, new[] { "Again. I'm just warming up.", "Is that all you weigh?" }),
            ["weight.rlose"] = (new[] { "嗯……该我了。", "没事，我沉得住。" }, new[] { "Hmm... my turn now.", "Fine. I can take it." }),
            // 稻草人：越挫越勇
            ["scarecrow.pick"] = (new[] { "算我一个！", "我还能打！" }, new[] { "Count me in!", "I can still fight!" }),
            ["scarecrow.win"] = (new[] { "哈哈！稻草也扎人！" }, new[] { "Haha! Straw can sting!" }),
            ["scarecrow.lose"] = (new[] { "哎哟……散架了……" }, new[] { "Ow... falling apart..." }),
            ["scarecrow.champ"] = (new[] { "乌鸦们，看见没！哈哈！" }, new[] { "See that, crows? Haha!" }),
            ["scarecrow.rwin"] = (new[] { "哈！再来再来！", "看见没，还有呢！" }, new[] { "Ha! Again, again!", "See that? There's more!" }),
            ["scarecrow.rlose"] = (new[] { "呸！才掉几根草！", "不疼不疼！再来！" }, new[] { "Pah! Just a few straws!", "Didn't hurt! Come on!" }),
            // 纸鸢：轻快、嘴欠
            ["kite.pick"] = (new[] { "跟得上我吗？", "起风了！" }, new[] { "Try to keep up.", "The wind is up!" }),
            ["kite.win"] = (new[] { "嘻嘻，太慢啦～" }, new[] { "Hehe, too slow!" }),
            ["kite.lose"] = (new[] { "呜……线断了……" }, new[] { "Aww... my string snapped." }),
            ["kite.champ"] = (new[] { "哇——风都替我高兴！" }, new[] { "Whoa! The wind cheers!" }),
            ["kite.rwin"] = (new[] { "嘻嘻，再飞一圈？", "就这？追我呀～" }, new[] { "Hehe, one more lap?", "That's it? Catch me!" }),
            ["kite.rlose"] = (new[] { "哼！刚才风不对！", "不算不算，再来！" }, new[] { "Hmph! The wind was wrong!", "Doesn't count! Again!" }),
            // 竹竿侠：侠客腔
            ["bamboo.pick"] = (new[] { "在下愿往。", "请赐教。" }, new[] { "I shall go.", "Show me your skill." }),
            ["bamboo.win"] = (new[] { "承让，承让。" }, new[] { "Well fought, well fought." }),
            ["bamboo.lose"] = (new[] { "惭愧……是在下输了。" }, new[] { "Alas... the loss is mine." }),
            ["bamboo.champ"] = (new[] { "此带……受之有愧。" }, new[] { "I am... truly honored." }),
            ["bamboo.rwin"] = (new[] { "得罪了，请再来。", "阁下，还未尽兴吧？" }, new[] { "Forgive me. Once more, please.", "Surely you have more?" }),
            ["bamboo.rlose"] = (new[] { "好身手！再领教。", "是在下大意了。" }, new[] { "Well struck! Once more.", "I was careless." }),
            // 铁甲将军：武将念白
            ["iron.pick"] = (new[] { "末将听令！", "末将愿打头阵！" }, new[] { "At your command!", "I'll lead the charge!" }),
            ["iron.win"] = (new[] { "哈哈哈！阵前无敌！" }, new[] { "Hahaha! Unbroken!" }),
            ["iron.lose"] = (new[] { "唔……甲……裂了。" }, new[] { "Urgh... my armor cracked." }),
            ["iron.champ"] = (new[] { "哈哈哈！此战，当记一功！" }, new[] { "Hahaha! A worthy victory!" }),
            ["iron.rwin"] = (new[] { "哈哈！再来战！", "敌将，可敢再战！" }, new[] { "Haha! Come again!", "Do you dare fight on?" }),
            ["iron.rlose"] = (new[] { "哼！胜败乃兵家常事！", "末将……还能再战！" }, new[] { "Hmph! One battle is not the war!", "I can... still fight!" }),
            // 不倒翁：憨、爱笑
            ["tumbler.pick"] = (new[] { "嘿，选我选我！", "我可倒不了～" }, new[] { "Hey, pick me!", "I never fall!" }),
            ["tumbler.win"] = (new[] { "嘿嘿，倒不了～" }, new[] { "Hehe, can't topple me!" }),
            ["tumbler.lose"] = (new[] { "哎呀……这回真倒了。" }, new[] { "Oh no... I'm really down." }),
            ["tumbler.champ"] = (new[] { "哟呵～转一圈庆祝！" }, new[] { "Woohoo! Victory spin!" }),
            ["tumbler.rwin"] = (new[] { "嘿嘿，再来一次嘛～", "好玩好玩，还要！" }, new[] { "Hehe, one more time!", "Fun, fun! More!" }),
            ["tumbler.rlose"] = (new[] { "哎哟～晃一晃又起来啦！", "没倒没倒，再来！" }, new[] { "Whoa~ wobble and I'm back up!", "Not down! Again!" }),
        };
        // 最终战那一句：每只木偶都说同一句话（配音各用各的嗓子）。语气 = 吃惊、迟疑
        static readonly (string[] zh, string[] en) mirror = (new[] { "咦？！……和我一模一样？" }, new[] { "Huh?! You're... just like me?" });
        static readonly Dictionary<string, int> last = new();

        static (string[] zh, string[] en)? Find(string kit, string ev) => ev == "mirror" ? mirror : all.TryGetValue(kit + "." + ev, out var v) ? v : ((string[], string[])?)null;

        /// <summary>挑一句（同一时刻有两句的，不连着说同一句）。返回文字和它是第几句（配音文件按这个编号找）。</summary>
        public static (string text, int idx) Pick(string kit, string ev)
        {
            var f = Find(kit, ev); if (f == null) return (null, 0);
            var arr = SBUI.En ? f.Value.en : f.Value.zh; if (arr.Length == 0) return (null, 0);
            string key = kit + "." + ev; int i = Random.Range(0, arr.Length);
            if (arr.Length > 1 && last.TryGetValue(key, out int l) && l == i) i = (i + 1) % arr.Length;
            last[key] = i;
            return (arr[i], i);
        }

        static readonly Dictionary<string, AudioClip> clips = new();
        /// <summary>这一句的配音（没有就返回空）。</summary>
        public static AudioClip Clip(string kit, string ev, int idx)
        {
            string name = $"{kit}_{ev}_{(SBUI.En ? "en" : "zh")}_{idx + 1}";
            if (!clips.TryGetValue(name, out var c)) { c = Resources.Load<AudioClip>("Voice/" + name); clips[name] = c; }
            return c;
        }

        /// <summary>开场把全部配音读进内存（两种语言都读：设置里能随时换语言），到说话那一下不用再等。</summary>
        public static void Preload()
        {
            foreach (var c in Resources.LoadAll<AudioClip>("Voice")) { clips[c.name] = c; if (c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData(); }
        }

        /// <summary>全部中文台词（预热字库用）。</summary>
        public static string AllText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in all) foreach (var s in kv.Value.zh) sb.Append(s);
            foreach (var s in mirror.zh) sb.Append(s);
            return sb.ToString();
        }

        /// <summary>全部台词的清单（生成配音用）：文件名、木偶、语言、文字。</summary>
        public static List<(string file, string kit, string lang, string text)> Manifest()
        {
            var list = new List<(string, string, string, string)>();
            void Add(string kit, string ev, (string[] zh, string[] en) v)
            {
                for (int i = 0; i < v.zh.Length; i++) list.Add(($"{kit}_{ev}_zh_{i + 1}", kit, "zh", v.zh[i]));
                for (int i = 0; i < v.en.Length; i++) list.Add(($"{kit}_{ev}_en_{i + 1}", kit, "en", v.en[i]));
            }
            foreach (var kv in all) { var q = kv.Key.Split('.'); Add(q[0], q[1], kv.Value); }
            foreach (var k in SBKit.All) Add(k.id, "mirror", mirror);
            return list;
        }
    }
}
