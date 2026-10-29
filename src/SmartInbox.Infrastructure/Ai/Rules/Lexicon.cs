using SmartInbox.Domain;

namespace SmartInbox.Infrastructure.Ai.Rules;

/// <summary>
/// Built-in bilingual vocabulary for the rule engine. Department keywords come from the database
/// (Department.Keywords); this table adds synonyms and Arabic equivalents for common keywords so
/// admins only need to maintain one short English list.
/// </summary>
public static class Lexicon
{
    public static readonly IReadOnlyDictionary<string, string[]> KeywordExpansions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["minister"] = ["his excellency", "your excellency", "h.e.", "معالي", "الوزير", "وزير"],
        ["invitation"] = ["invite", "keynote", "forum", "دعوه", "يسرنا دعوه", "لحضور"],
        ["cabinet"] = ["council of ministers", "مجلس الوزراء"],
        ["ceremony"] = ["حفل"],
        ["contract"] = ["agreement", "memorandum of understanding", "عقد", "اتفاقيه", "مذكره تفاهم"],
        ["court"] = ["hearing", "claimant", "defendant", "summon", "محكمه", "جلسه", "دعوي", "المدعي"],
        ["lawsuit"] = ["damages", "breach of contract", "قضيه"],
        ["legal"] = ["liability", "penalt", "clause", "قانوني", "القانونيه", "بنود", "غرامات", "المسؤوليه"],
        ["regulation"] = ["compliance", "لائحه", "تشريع"],
        ["invoice"] = ["tax invoice", "amount due", "total due", "bill to", "فاتوره", "الاجمالي المستحق", "ضريبه القيمه المضافه"],
        ["payment"] = ["iban", "payment terms", "سداد", "الدفع"],
        ["tender"] = ["bid", "procurement", "rfp", "مناقصه", "عطاء", "المشتريات"],
        ["purchase order"] = ["po-", "امر الشراء"],
        ["vendor"] = ["supplier", "مورد", "توريد"],
        ["budget"] = ["ميزانيه", "موازنه"],
        ["employee"] = ["staff", "employee no", "performance rating", "line manager", "موظف", "الموظفين"],
        ["recruitment"] = ["hiring", "vacancy", "nominate", "nomination", "cv", "cvs", "توظيف", "تعيين", "ترشيح"],
        ["training"] = ["course", "certification", "تدريب", "تدريبي", "برنامج تدريبي", "الالتحاق"],
        ["leave"] = ["flexible work", "remote work", "working hours", "اجازه", "العمل المرن", "عن بعد", "الدوام", "الحضور والانصراف"],
        ["salary"] = ["allowance", "راتب", "بدلات"],
        ["secondment"] = ["انتداب", "اعاره"],
        ["system"] = ["portal", "platform", "server", "outage", "error code", "نظام", "منصه", "انظمه"],
        ["integration"] = ["api", "single sign-on", "sso", "sandbox", "credentials", "ربط", "تكامل"],
        ["cybersecurity"] = ["vulnerability", "cve", "vpn", "patch", "malware", "security advisory", "ثغره", "الامن السيبراني", "اختراق"],
        ["software"] = ["application", "تطبيق", "برمجيات"],
        ["network"] = ["شبكه"],
        ["data"] = ["database", "قاعده بيانات"],
        ["complaint"] = ["complain", "dissatisf", "nobody answers", "no response", "شكوي", "اشتكي", "لم اتلق"],
        ["feedback"] = ["ملاحظات المتعاملين"],
        ["service request"] = ["application number", "رقم الطلب", "طلب اصدار"],
        ["customer"] = ["citizen", "contact centre", "contact center", "hotline", "متعامل", "مركز الاتصال"],
        ["delay"] = ["stuck", "for six weeks", "تاخر", "تاخير", "قيد المراجعه"],
        ["circular"] = ["all ministries", "all federal", "تعميم", "جميع الوزارات", "جميع الجهات"],
        ["policy"] = ["سياسه"],
        ["kpi"] = ["key performance indicator", "performance indicators", "المؤشرات", "مؤشرات الاداء"],
        ["report"] = ["quarterly", "تقرير", "ربع سنوي"],
        ["strategy"] = ["strategic", "research", "partnership", "الاستراتيجيه", "استراتيجي"],
        ["initiative"] = ["مبادره", "المبادرات"],
    };

    /// <summary>Words that signal each category, with weights. Evaluated in order; ties go to the earlier one.</summary>
    public static readonly (DocumentCategory Category, (string Term, int Weight)[] Terms)[] CategoryTerms =
    [
        (DocumentCategory.Invoice, [("tax invoice", 4), ("invoice", 3), ("amount due", 2), ("total due", 2), ("فاتوره ضريبيه", 4), ("فاتوره", 3), ("الاجمالي المستحق", 2)]),
        (DocumentCategory.Legal, [("court", 3), ("lawsuit", 3), ("claimant", 3), ("defendant", 3), ("legal", 2), ("summon", 2),
            ("محكمه", 3), ("دعوي", 3), ("القانونيه", 2), ("قانوني", 2), ("مسوده العقد", 3), ("بنود", 1)]),
        (DocumentCategory.Complaint, [("complaint", 4), ("complain", 3), ("dissatisf", 2), ("nobody answers", 2), ("not been resolved", 2), ("fail", 1), ("error", 1),
            ("شكوي", 4), ("اشتكي", 3), ("لم اتلق", 2), ("تاخر", 1)]),
        (DocumentCategory.Invitation, [("invitation", 3), ("invite", 3), ("honoured to invite", 4), ("keynote", 2), ("دعوه", 3), ("يسرنا دعوه", 4), ("لحضور", 2)]),
        (DocumentCategory.Circular, [("circular", 4), ("all ministries", 3), ("all federal", 3), ("all entities", 2), ("تعميم", 4), ("جميع الوزارات", 3), ("جميع الجهات", 3)]),
        (DocumentCategory.Request, [("request", 2), ("kindly", 1), ("please provide", 2), ("we would appreciate", 2), ("we propose", 2), ("proposal", 1),
            ("طلب", 2), ("يرجي", 2), ("نرجو", 2), ("ارجو", 2), ("التكرم", 1)]),
        (DocumentCategory.Other, [("thank", 3), ("advisory", 3), ("شكر", 3), ("تحذير", 2)]),
    ];

    public static readonly string[] UrgentTerms =
        ["urgent", "immediately", "as soon as possible", "asap", "critical", "عاجل", "فوري", "فورا", "في اقرب وقت"];

    public static readonly string[] LowPriorityTerms = ["thank you", "thanks", "for your information", "fyi", "نشكر", "شكر", "للعلم", "للاحاطه"];

    public static readonly string[] EscalationTerms = ["frozen", "legal action", "escalat", "damages", "penalt", "تصعيد", "غرامات"];

    /// <summary>Words shortly before a date that mark it as a deadline rather than a reference date.</summary>
    public static readonly string[] DeadlineCues =
        ["by ", "no later than", "not later than", "deadline", "due", "before", "until", "submit", "موعد اقصاه", "بحلول", "قبل", "حتي", "في موعد"];

    public static readonly string[] ActionCues =
        ["please", "kindly", "we request", "request that", "we would appreciate", "required", "must", "are required", "provide",
         "يرجي", "نرجو", "ارجو", "المطلوب", "التكرم", "نامل"];

    public static readonly string[] ClosingLines =
        ["regards", "sincerely", "respectfully", "yours", "best regards", "kind regards", "with highest regards",
         "وتفضلوا", "مع خالص", "مع التحيه", "والسلام", "تحيه طيبه"];

    public static readonly Dictionary<DocumentCategory, string> CategoryAr = new()
    {
        [DocumentCategory.Request] = "طلب", [DocumentCategory.Complaint] = "شكوى", [DocumentCategory.Invitation] = "دعوة",
        [DocumentCategory.Circular] = "تعميم", [DocumentCategory.Invoice] = "فاتورة", [DocumentCategory.Legal] = "مراسلة قانونية",
        [DocumentCategory.Other] = "مراسلة",
    };
}
