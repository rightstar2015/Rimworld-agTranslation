namespace AgentTranslation.Core
{
    public enum FieldKind
    {
        /// <summary>string 欄位。</summary>
        String,
        /// <summary>List&lt;string&gt; 等字串集合。</summary>
        StringList,
        /// <summary>自訂型別的清單（li 子節點各為一個物件）。</summary>
        ObjectList,
        /// <summary>自訂型別的單一物件。</summary>
        Object,
        /// <summary>Def 參照或其他不會遞迴的型別（數值、列舉、Type、字典…）。</summary>
        Other,
    }

    public sealed class FieldShape
    {
        public string Name { get; set; } = string.Empty;
        public FieldKind Kind { get; set; }
        /// <summary>Object：欄位型別；ObjectList：元素型別。完整名稱（Namespace.Type）。</summary>
        public string? ValueTypeFullName { get; set; }
        public bool NoTranslate { get; set; }
        public bool Unsaved { get; set; }
        public bool MustTranslate { get; set; }
        public bool MayTranslate { get; set; }
        /// <summary>任務系統的 SlateRef 欄位，階段 1 不處理。</summary>
        public bool SlateRef { get; set; }
        public bool TranslationCanChangeCount { get; set; }
    }

    public sealed class TypeShape
    {
        public string FullName { get; set; } = string.Empty;
        public bool IsDef { get; set; }
    }

    /// <summary>
    /// 遊戲與模組組件的型別資訊（欄位、屬性）。伺服器端以 System.Reflection.Metadata 讀取組件中繼資料實作，
    /// 不載入、不執行任何組件程式碼。
    /// </summary>
    public interface ITypeSchema
    {
        /// <summary>依 XML 中的名稱（Defs 根元素名稱或 Class 屬性）解析型別，規則同 GenTypes.GetTypeInAnyAssembly。</summary>
        TypeShape? ResolveXmlTypeName(string name);

        TypeShape? GetByFullName(string fullName);

        /// <summary>找欄位：不分大小寫、含基底型別、支援 LoadAlias。</summary>
        FieldShape? FindField(TypeShape type, string xmlName);
    }
}
