using Microsoft.CodeAnalysis;

namespace Salvavida.Generator
{
#pragma warning disable RS2008 // Enable analyzer release tracking
    internal static class DiagnosticDescriptors
    {
        const string CATEGORY = "SalvalidaCodeGeneration";

        public static readonly DiagnosticDescriptor MustBePartial = new(
            id: "SV001",
            title: "标记Savable特性的对象必须是partial类",
            messageFormat: "标记了Savable对象的类：'{0}'，必须是parital类",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TooManySerializers = new(
            id: "SV002",
            title: "标记Savable特性的对象包含了超过了1个序列化的能力",
            messageFormat: "标记了Savable对象的类：'{0}'，包含了超过了1个序列化的能力",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NestingNotAllowed = new(
            id: "SV003",
            title: "标记Savable特性的对象不能是内嵌类",
            messageFormat: "标记了Savable对象的类：'{0}'，不能是内嵌类",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidMemberName = new(
            id: "SV004",
            title: "不支持的成员，成员名称的首个字符必须是小写ASCII字母或下划线",
            messageFormat: "成员 '{0}' 不受支持：，它的首个字符必须是小写ASCII字母或下划线",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PropertyNameIsInUse = new(
            id: "SV005",
            title: "根据成员名称生成的属性名称已经被使用了",
            messageFormat: "成员 '{0}' 生成的属性名称 '{1}'：已经被使用了，请更名或给该成员使用[PropertyName(\"newName\")]特性来自定义名称",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PropertyNameAttributeNotSupported = new(
            id: "SV006",
            title: "一行字段声明了多个变量的情况下不支持使用PropertyName特性",
            messageFormat: "一行字段声明了多个变量的情况下不支持使用PropertyName特性",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CantResolvePropetyName = new(
            id: "SV007",
            title: "PropertyName特性赋值时目前只支持使用字符串表达式",
            messageFormat: "PropertyName特性赋值时目前只支持使用字符串表达式，例如：[PropertyName(\"prop1\")]",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AllCandidateNamesAreUnavailable = new(
            id: "SV007",
            title: "Salvavida自动生成的所有候选名称都已重复，请添加PropertyName特性以自定义名称",
            messageFormat: "Salvavida自动生成的所有候选名称都已重复，请添加PropertyName特性以自定义名称",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UJ_PrivateKeywoardRecommended = new(
            id: "SV100",
            title: "推荐使用private访问限制符+SerializeField特性",
            messageFormat: "推荐使用private访问限制符+SerializeField特性，避免意外操作数据本体",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UJ_ForgetSerializerFieldAttribute = new(
            id: "SV101",
            title: "你可能忘记在非公开字段中添加SerializeField特性",
            messageFormat: "当前字段可能忘记添加SerializeField特性，如果你本意如此，可使用Ignore特性忽略当前字段",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UJ_NonSerializedAttributeRequired = new(
            id: "SV102",
            title: "公开且标记为SaveSeparately特性的字段需要同时添加NonSerialized特性",
            messageFormat: "当前字段被标记为SaveSeparately特性,需要同时添加NonSerialized特性",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UJ_HaveToRemoveSerializeFieldAttribute = new(
            id: "SV103",
            title: "非公开且被标记为为SaveSeparately特性的字段需要移除SerializeField特性",
            messageFormat: "当前字段被标记为SaveSeparately特性,需要将已经添加的SerializeField特性移除",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MP_IgnoreAttributeRequired = new(
            id: "SV201",
            title: "公开且标记为SaveSeparately特性的字段需要同时添加MemoryPackIgnore特性",
            messageFormat: "当前字段被标记为SaveSeparately特性，需要同时添加MemoryPackIgnore特性",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MP_HaveToRemoveIncludeAttribute = new(
            id: "SV202",
            title: "非公开且被标记为为SaveSeparately特性的字段需要移除MemoryPackInclude特性",
            messageFormat: "当前字段被标记为SaveSeparately特性,需要将已经添加的MemoryPackInclude特性移除",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MP_ForgetMemoryPackableIncludeAttribute = new(
            id: "SV203",
            title: "你可能忘记在非公开字段中添加MemoryPackInclude特性",
            messageFormat: "当前字段可能忘记添加MemoryPackInclude特性，如果你本意如此，可使用Ignore特性忽略当前字段",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MP_PrivateKeywoardRecommended = new(
            id: "SV204",
            title: "推荐使用private访问限制符+MemoryPackInclude特性",
            messageFormat: "推荐使用private访问限制符+MemoryPackInclude特性特性，避免意外操作数据本体",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
#pragma warning restore RS2008 // Enable analyzer release tracking
}
