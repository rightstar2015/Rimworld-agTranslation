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
        /// <summary>不合格，不得寫入。</summary>
        public List<ValidationIssue> Rejects { get; } = new List<ValidationIssue>();
        /// <summary>可疑，寫入但列入報告。</summary>
        public List<ValidationIssue> Flags { get; } = new List<ValidationIssue>();
        public bool Accepted => Rejects.Count == 0;
    }

    public sealed class ValidatorOptions
    {
        /// <summary>允許譯文與原文相同的專有名詞（不分大小寫）。</summary>
        public ISet<string> ProperNounWhitelist { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int MaxLength { get; set; } = 20000;
    }

    /// <summary>譯文驗證。規則只能變嚴格；放寬必須附上遊戲實際接受該格式的證據。</summary>
    public static class Validator
    {
        public static ValidationOutcome Validate(string source, string? translation, ValidatorOptions? options = null)
        {
            options = options ?? new ValidatorOptions();
            var outcome = new ValidationOutcome();
            var text = translation ?? string.Empty;

            if (text.Trim().Length == 0)
            {
                outcome.Rejects.Add(new ValidationIssue("empty", "譯文是空的。"));
                return outcome;
            }
            if (text.Length > options.MaxLength)
            {
                outcome.Rejects.Add(new ValidationIssue("too_long", "譯文超過 " + options.MaxLength + " 字元上限。"));
                return outcome;
            }

            var badChar = FindInvalidXmlChar(text);
            if (badChar != null)
            {
                outcome.Rejects.Add(new ValidationIssue("invalid_xml_chars", "譯文含 XML 不允許的字元 " + badChar + "。"));
            }
            if (text.Trim() == "TODO")
            {
                outcome.Rejects.Add(new ValidationIssue("placeholder_text", "遊戲會把 TODO 當成「尚未翻譯」的占位文字。"));
            }

            foreach (var pair in Tokens.Diff(source, text))
            {
                var code = CodeFor(pair.Key);
                var parts = new List<string>();
                if (pair.Value.Missing.Count > 0)
                {
                    parts.Add("缺少 " + string.Join("、", pair.Value.Missing));
                }
                if (pair.Value.Extra.Count > 0)
                {
                    parts.Add("多出 " + string.Join("、", pair.Value.Extra));
                }
                outcome.Rejects.Add(new ValidationIssue(code, "與原文不一致：" + string.Join("；", parts) + "。"));
            }

            var stripped = Tokens.StripTokens(text);
            var simplified = SimplifiedChars.FindIn(stripped);
            if (simplified.Count > 0)
            {
                outcome.Rejects.Add(new ValidationIssue("simplified_chars", "含簡體字：" + string.Join("", simplified.Distinct()) + "。請改用繁體（台灣用字）。"));
            }

            if (string.Equals(text.Trim(), source.Trim(), StringComparison.Ordinal) && source.Any(IsAsciiLetter) && !options.ProperNounWhitelist.Contains(source.Trim()))
            {
                outcome.Flags.Add(new ValidationIssue("identical_to_source", "譯文與原文相同且含英文字母；若是專有名詞請加入白名單，否則請翻譯。"));
            }

            var srcLen = Tokens.StripTokens(source).Trim().Length;
            var dstLen = stripped.Trim().Length;
            if (srcLen >= 12)
            {
                var ratio = (double)dstLen / srcLen;
                if (ratio < 0.12 || ratio > 1.6)
                {
                    outcome.Flags.Add(new ValidationIssue("length_anomaly", "譯文長度與原文比例異常（" + ratio.ToString("0.00") + "）。"));
                }
            }
            else if (dstLen > srcLen * 3 + 6)
            {
                outcome.Flags.Add(new ValidationIssue("length_anomaly", "短原文的譯文過長。"));
            }

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
    /// 階段 1 的簡體字偵測：只列出常見且「繁體中文不使用」的簡化字。
    /// 這是暫時的保守清單，階段 2 會換成以 OpenCC 字典產生的完整集合。
    /// </summary>
    public static class SimplifiedChars
    {
        private const string Chars =
            "这们说时为个国过对还发经动现么关点与东车长门问间听见风飞马鸟鱼龙会来给让应该没头样实开学进将军战击伤队枪弹药装备护态质级设计选择确认显储运输电资产业农场养续断杂负担损坏维"
            + "应废库开弃张弯归当录彻忆忧怀态总恋恶悬惊惧惨惯戏战扑执扩扫扬扰抚抛抢护报担拟拢拣拥拦拧拨择挡挤挥损捡换掳掷搀摄摆摇摊撵擞攒敌敛数斋断旧时旷昙显晒晓晕暂机杀杂权条来杨极构枢枣枪栋栏树样档桥桨梦检棂椭楼榄樱欢欧歼殴毕毙汇汉汤沟没泼泽洁浅测济浑浓涛涝润涨渊渐渔渗湾湿溃满滤滥滨滩潆灭灯灵灾灿炉点炼烂烛烦烧热焕爱爷牍牵犹狈狮独狭狱猎献获环现玛琐瑶瓒电畅疗疮疯瘫瘾监盘睁矫码砖础硕确碍礼祷祸离秃秆积称种稳穷窃窍窑窜窝竖竞笔笼筛筹签简粮紧纠红纤约级纪纫纬纯纱纲纳纵纷纸纹纺线练组细织终绍经绑结绕绘给络绝统绢绣继绩绪续绳维绵综绽绿缀缆缓编缘缚缝缠缩缴罗罚罢羁翘联聪肃肠肤肾肿胀胁胆胶脏脑脸腻舆舰舱艰艺节芜苇苍苏茎荐荡荣荤药莱莲获萝萤营萧萨蓝蔷虏虑虫虽虾蚀蚁蚂蛮蝇衅补衬袄袜袭装裤见观规觅视览觉誉计订认讨让训议讯记讲讳许论设访诀证评识诈诉诊词译试诗诚话诞询该详诧诫语误诱说请诸诺读课谁调谅谈谊谋谍谎谐谓谜谢谣谦谨谱贝贞负贡财责贤败账货质贩贪贫购贯贱贴贵贷贸费贺贼资赋赌赏赔赖赚赛赞赠赢赵赶趋跃践踪躏车轨轩转轮软轰轴轻载较辅辆辈辉辐辑输辖辞边辽达迁过迈运还这进远违连迟选逊递逻遗遥邓邮邻郑酱酿释针钓钙钟钢钥钦钩钮钱钻铁铃铅铜铭铺链销锁锄锅锈锋锐错锡锦键锯锻镇镜长门闪闭问闯闲间闷闸闹闻阀阁阅队阳阴阵阶际陆陈险随隐隶难雾页顶项顺须顽顾顿预领颇频颗题颜额风飘飞饥饭饮饰饱饲饿馆马驭驱驳驶驻驾骂验骑骗骚鱼鲜鸟鸡鸣鸭鸽鹅鹰麦齐齿龄龙龟";

        private static HashSet<char> Set = new HashSet<char>(Chars);

        /// <summary>
        /// 以完整集合取代內建清單。伺服器啟動時以 OpenCC STCharacters 產生：
        /// 「轉換候選中不含自己」的字才算簡體專用字（例如 发→發髮 算；台→臺檯颱台 不算）。
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
}
