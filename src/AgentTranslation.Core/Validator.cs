using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace AgentTranslation.Core
{
    public sealed class ValidationIssue
    {
        public ValidationIssue(string code, string detail)
        {
            Code = code;
            Detail = detail;
        }

        public string Code { get; }
        public string Detail { get; }
    }

    public sealed class ValidationOutcome
    {
        /// <summary>Rejected: must not be written.</summary>
        public List<ValidationIssue> Rejects { get; } = new List<ValidationIssue>();
        /// <summary>Suspicious: written, but listed in reports.</summary>
        public List<ValidationIssue> Flags { get; } = new List<ValidationIssue>();
        public bool Accepted => Rejects.Count == 0;
    }

    /// <summary>Script of the target language; decides which wrong-script characters are rejected.</summary>
    public enum TargetScript
    {
        /// <summary>ChineseSimplified: Traditional-only characters are rejected.</summary>
        Simplified,
        /// <summary>ChineseTraditional (Taiwan): Simplified-only characters are rejected.</summary>
        Traditional,
    }

    public sealed class ValidatorOptions
    {
        /// <summary>Proper nouns allowed to stay identical to the source (case-insensitive).</summary>
        public ISet<string> ProperNounWhitelist { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int MaxLength { get; set; } = 20000;
        public TargetScript Script { get; set; } = TargetScript.Simplified;
    }

    /// <summary>Translation validation. Rules may only get stricter; relaxing one needs evidence that the game accepts the format.</summary>
    public static class Validator
    {
        public static ValidationOutcome Validate(string source, string? translation, ValidatorOptions? options = null)
        {
            options = options ?? new ValidatorOptions();
            var outcome = new ValidationOutcome();
            var text = translation ?? string.Empty;

            if (text.Trim().Length == 0)
            {
                outcome.Rejects.Add(new ValidationIssue("empty", "Translation is empty."));
                return outcome;
            }
            if (text.Length > options.MaxLength)
            {
                outcome.Rejects.Add(new ValidationIssue("too_long", "Translation exceeds the " + options.MaxLength + " character limit."));
                return outcome;
            }

            var badChar = FindInvalidXmlChar(text);
            if (badChar != null)
            {
                outcome.Rejects.Add(new ValidationIssue("invalid_xml_chars", "Translation contains a character not allowed in XML: " + badChar + "."));
            }
            if (text.Trim() == "TODO")
            {
                outcome.Rejects.Add(new ValidationIssue("placeholder_text", "The game treats TODO as an untranslated placeholder."));
            }

            foreach (var pair in Tokens.Diff(source, text))
            {
                var code = CodeFor(pair.Key);
                var parts = new List<string>();
                if (pair.Value.Missing.Count > 0)
                {
                    parts.Add("missing " + string.Join(", ", pair.Value.Missing));
                }
                if (pair.Value.Extra.Count > 0)
                {
                    parts.Add("extra " + string.Join(", ", pair.Value.Extra));
                }
                outcome.Rejects.Add(new ValidationIssue(code, "Does not match the source: " + string.Join("; ", parts) + "."));
            }

            var stripped = Tokens.StripTokens(text);
            if (options.Script == TargetScript.Traditional)
            {
                var simplified = SimplifiedChars.FindIn(stripped);
                if (simplified.Count > 0)
                {
                    outcome.Rejects.Add(new ValidationIssue("simplified_chars", "Contains Simplified characters: " + string.Join("", simplified.Distinct()) + ". Use Traditional characters (Taiwan usage)."));
                }
            }
            else
            {
                var traditional = TraditionalChars.FindIn(stripped);
                if (traditional.Count > 0)
                {
                    outcome.Rejects.Add(new ValidationIssue("traditional_chars", "Contains Traditional characters: " + string.Join("", traditional.Distinct()) + ". Use Simplified characters."));
                }
            }

            if (string.Equals(text.Trim(), source.Trim(), StringComparison.Ordinal) && source.Any(IsAsciiLetter) && !options.ProperNounWhitelist.Contains(source.Trim()))
            {
                outcome.Flags.Add(new ValidationIssue("identical_to_source", "Translation equals the source and contains Latin letters; add proper nouns to the whitelist, otherwise translate it."));
            }

            var srcLen = Tokens.StripTokens(source).Trim().Length;
            var dstLen = stripped.Trim().Length;
            if (srcLen >= 12)
            {
                var ratio = (double)dstLen / srcLen;
                if (ratio < 0.12 || ratio > 1.6)
                {
                    outcome.Flags.Add(new ValidationIssue("length_anomaly", "Unusual translation/source length ratio (" + ratio.ToString("0.00") + ")."));
                }
            }
            else if (dstLen > srcLen * 3 + 6)
            {
                outcome.Flags.Add(new ValidationIssue("length_anomaly", "Translation is too long for a short source."));
            }

            return outcome;
        }

        /// <summary>
        /// Validates the translation of a key used in code that has no English source. Placeholders are checked against
        /// the number of format arguments the code passes (when known) instead of a source text. Always flagged as
        /// inferred, so the user can spot-check these entries.
        /// </summary>
        public static ValidationOutcome ValidateCodeKey(string? translation, int? argCount, ValidatorOptions? options = null)
        {
            var text = translation ?? string.Empty;
            var braces = Tokens.Extract(text).Where(t => t.Kind == TokenKind.Brace).Select(t => t.Text).Distinct().ToList();
            // Run the general checks with a pseudo-source that holds exactly the translation's braces, so they pass the
            // source comparison and are checked against the argument count below instead.
            var outcome = Validate(string.Join(" ", braces), text, options);
            outcome.Flags.RemoveAll(f => f.Code == "identical_to_source" || f.Code == "length_anomaly");
            if (!outcome.Accepted && outcome.Rejects.Any(r => r.Code == "empty" || r.Code == "too_long"))
            {
                return outcome;
            }
            foreach (var b in braces)
            {
                var inner = b.Trim('{', '}');
                var index = inner.Split(':')[0];
                if (argCount == 0)
                {
                    outcome.Rejects.Add(new ValidationIssue("placeholder_mismatch", "The code passes no arguments with this key, so " + b + " would show literally. Remove it."));
                }
                else if (argCount != null && int.TryParse(index, out var i) && (i < 0 || i >= argCount))
                {
                    outcome.Rejects.Add(new ValidationIssue("placeholder_mismatch", "The code passes " + argCount + " argument(s); " + b + " is out of range (use {0}.." + "{" + (argCount - 1) + "})."));
                }
            }
            if (Tokens.StripTokens(text).Trim().Length > 300)
            {
                outcome.Flags.Add(new ValidationIssue("length_anomaly", "Unusually long for a text inferred from a key name."));
            }
            outcome.Flags.Add(new ValidationIssue("code_key_inferred", "No English source exists; the text was inferred from the key name and its use in code."));
            return outcome;
        }

        private static string CodeFor(TokenKind kind)
        {
            switch (kind)
            {
                case TokenKind.Brace: return "placeholder_mismatch";
                case TokenKind.Markup: return "markup_mismatch";
                case TokenKind.Tag: return "tag_mismatch";
                case TokenKind.Grammar: return "grammar_token_mismatch";
                case TokenKind.Newline: return "newline_mismatch";
                default: return "token_mismatch";
            }
        }

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static string? FindInvalidXmlChar(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], c))
                {
                    i++;
                    continue;
                }
                if (!XmlConvert.IsXmlChar(c))
                {
                    return "U+" + ((int)c).ToString("X4");
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Simplified-only characters (used in Traditional mode). The built-in list is a small conservative fallback;
    /// the server replaces it on startup with the full set derived from OpenCC.
    /// </summary>
    public static class SimplifiedChars
    {
        private const string Chars =
            "这们说时为个国过对还发经动现么关点与东车长门问间听见风飞马鸟鱼龙会来给让应该没头样实开学进将军战击伤队枪弹药装备护态质级设计选择确认显储运输电资产业农场养续断杂负担损坏维"
            + "应废库开弃张弯归当录彻忆忧怀态总恋恶悬惊惧惨惯戏战扑执扩扫扬扰抚抛抢护报担拟拢拣拥拦拧拨择挡挤挥损捡换掳掷搀摄摆摇摊撵擞攒敌敛数斋断旧时旷昙显晒晓晕暂机杀杂权条来杨极构枢枣枪栋栏树样档桥桨梦检棂椭楼榄樱欢欧歼殴毕毙汇汉汤沟没泼泽洁浅测济浑浓涛涝润涨渊渐渔渗湾湿溃满滤滥滨滩潆灭灯灵灾灿炉点炼烂烛烦烧热焕爱爷牍牵犹狈狮独狭狱猎献获环现玛琐瑶瓒电畅疗疮疯瘫瘾监盘睁矫码砖础硕确碍礼祷祸离秃秆积称种稳穷窃窍窑窜窝竖竞笔笼筛筹签简粮紧纠红纤约级纪纫纬纯纱纲纳纵纷纸纹纺线练组细织终绍经绑结绕绘给络绝统绢绣继绩绪续绳维绵综绽绿缀缆缓编缘缚缝缠缩缴罗罚罢羁翘联聪肃肠肤肾肿胀胁胆胶脏脑脸腻舆舰舱艰艺节芜苇苍苏茎荐荡荣荤药莱莲获萝萤营萧萨蓝蔷虏虑虫虽虾蚀蚁蚂蛮蝇衅补衬袄袜袭装裤见观规觅视览觉誉计订认讨让训议讯记讲讳许论设访诀证评识诈诉诊词译试诗诚话诞询该详诧诫语误诱说请诸诺读课谁调谅谈谊谋谍谎谐谓谜谢谣谦谨谱贝贞负贡财责贤败账货质贩贪贫购贯贱贴贵贷贸费贺贼资赋赌赏赔赖赚赛赞赠赢赵赶趋跃践踪躏车轨轩转轮软轰轴轻载较辅辆辈辉辐辑输辖辞边辽达迁过迈运还这进远违连迟选逊递逻遗遥邓邮邻郑酱酿释针钓钙钟钢钥钦钩钮钱钻铁铃铅铜铭铺链销锁锄锅锈锋锐错锡锦键锯锻镇镜长门闪闭问闯闲间闷闸闹闻阀阁阅队阳阴阵阶际陆陈险随隐隶难雾页顶项顺须顽顾顿预领颇频颗题颜额风飘飞饥饭饮饰饱饲饿馆马驭驱驳驶驻驾骂验骑骗骚鱼鲜鸟鸡鸣鸭鸽鹅鹰麦齐齿龄龙龟";

        private static HashSet<char> Set = new HashSet<char>(Chars);

        /// <summary>
        /// Replaces the built-in list with a full set. The server builds it from OpenCC STCharacters: a character is
        /// Simplified-only when none of its conversion candidates is itself (发 -> 發/髮 counts; 台 -> 臺/檯/颱/台 does not).
        /// </summary>
        public static void UseSet(IEnumerable<char> chars)
        {
            Set = new HashSet<char>(chars);
        }

        public static int Count => Set.Count;

        public static List<char> FindIn(string text)
        {
            var found = new List<char>();
            foreach (var c in text)
            {
                if (Set.Contains(c))
                {
                    found.Add(c);
                }
            }
            return found;
        }
    }

    /// <summary>
    /// Traditional-only characters (used in Simplified mode). The built-in list is a small conservative fallback;
    /// the server replaces it on startup with the full set derived from OpenCC TSCharacters.
    /// </summary>
    public static class TraditionalChars
    {
        private const string Chars =
            "這們說時為個國過對還發經動現麼關點與東車長門問間聽見風飛馬鳥魚龍會來給讓應該沒頭樣實開學進將軍戰擊傷隊槍彈藥裝備護態質級設計選擇確認顯儲運輸電資產業農場養續斷雜負擔損壞維"
            + "廢庫棄張彎歸當錄徹憶憂懷總戀惡懸驚懼慘慣戲撲執擴掃揚擾撫拋搶報擬攏揀擁攔擰撥擋擠揮撿換擄擲攝擺搖攤敵斂數齋舊曠曬曉暈暫機殺權條楊極構樞棗棟欄樹檔橋槳夢檢樓欄櫻歡歐殲毆畢斃匯漢湯溝潑澤潔淺測濟渾濃濤澇潤漲淵漸漁滲灣濕潰滿濾濫濱灘滅燈靈災燦爐煉爛燭煩燒熱煥愛爺牽猶狽獅獨狹獄獵獻獲環瑣電暢療瘋癱癮監盤睜矯碼磚礎碩確礙禮禱禍離禿稈積稱種穩窮竊竅窯竄窩豎競筆籠篩籌簽簡糧緊糾紅纖約紀紉緯純紗綱納縱紛紙紋紡線練組細織終紹綁結繞繪絡絕統絹繡繼績緒繩綿綜綻綠綴纜緩編緣縛縫纏縮繳羅罰罷羈翹聯聰肅腸膚腎腫脹脅膽膠臟腦臉膩輿艦艙艱藝節蕪葦蒼蘇莖薦蕩榮葷萊蓮蘿螢營蕭薩藍薔虜慮蟲雖蝦蝕蟻螞蠻蠅釁補襯襖襪襲褲觀規覓視覽覺譽計訂認討訓議訊記講諱許論訪訣證評識詐訴診詞譯試詩誠話誕詢詳詫誡語誤誘請諸諾讀課誰調諒談誼謀諜謊諧謂謎謝謠謙謹譜貝貞負貢財責賢敗賬貨販貪貧購貫賤貼貴貸貿費賀賊資賦賭賞賠賴賺賽贊贈贏趙趕趨躍踐蹤躪軌軒轉輪軟轟軸輕載較輔輛輩輝輻輯輸轄辭邊遼達遷邁運遠違連遲選遜遞邏遺遙鄧郵鄰鄭醬釀釋針釣鈣鐘鋼鑰欽鉤鈕錢鑽鐵鈴鉛銅銘鋪鏈銷鎖鋤鍋鏽鋒銳錯錫錦鍵鋸鍛鎮鏡閃閉闖閒悶閘鬧聞閥閣閱陽陰陣階際陸陳險隨隱隸難霧頁頂項順須頑顧頓預領頗頻顆題顏額飄饑飯飲飾飽飼餓館馭驅駁駛駐駕罵驗騎騙騷鮮雞鳴鴨鴿鵝鷹麥齊齒齡龜";

        private static HashSet<char> Set = new HashSet<char>(Chars);

        /// <summary>Replaces the built-in list with a full set (characters whose OpenCC TSCharacters candidates never include themselves).</summary>
        public static void UseSet(IEnumerable<char> chars)
        {
            Set = new HashSet<char>(chars);
        }

        public static int Count => Set.Count;

        public static List<char> FindIn(string text)
        {
            var found = new List<char>();
            foreach (var c in text)
            {
                if (Set.Contains(c))
                {
                    found.Add(c);
                }
            }
            return found;
        }
    }
}