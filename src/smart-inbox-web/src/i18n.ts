import { createI18n } from 'vue-i18n'

const en = {
  app: { title: 'Smart Inbox', subtitle: 'Ministry of Digital Services – Demo', live: 'Live updates on', offline: 'Reconnecting…' },
  nav: { inbox: 'Inbox', needsReview: 'Needs review', ready: 'Ready to route', dashboard: 'Insights', theme: 'Theme', language: 'Language', dark: 'Dark', light: 'Light' },
  actions: {
    upload: 'New correspondence', reanalyze: 'Re-analyze', accept: 'Accept & route', override: 'Choose another department',
    route: 'Route', cancel: 'Cancel', back: 'Inbox', clear: 'Clear', open: 'Open', retry: 'Retry',
  },
  inbox: {
    title: 'Inbox', subtitle: 'AI reads every document and suggests where it should go. You decide.',
    all: 'All', results: '{n} documents', results1: '1 document', waiting: 'Waiting for department',
  },
  timeline: {
    title: 'Activity', system: 'System',
    Uploaded: 'Received', Analyzed: 'Analyzed, suggested {dept}', AnalysisFailed: 'Analysis failed',
    Routed: 'Routed to {dept}', Acknowledged: '{dept} acknowledged', Closed: '{dept} closed it',
    Reopened: '{dept} reopened it', EmailSent: 'Email sent', EmailFailed: 'Email could not be sent',
    Escalated: 'Overdue: escalated to the head of {dept}',
  },
  filters: { search: 'Search sender, reference or content…', status: 'Status', priority: 'Priority', department: 'Department' },
  status: { Uploaded: 'Uploaded', Processing: 'Analyzing', Analyzed: 'Ready to route', Routed: 'Routed', NeedsReview: 'Needs review', Failed: 'Failed' },
  priority: { Low: 'Low', Normal: 'Normal', High: 'High', Urgent: 'Urgent' },
  category: { Request: 'Request', Complaint: 'Complaint', Invitation: 'Invitation', Circular: 'Circular', Invoice: 'Invoice', Legal: 'Legal', Other: 'Other' },
  deadline: { overdue: 'Overdue {n}d', today: 'Due today', tomorrow: 'Due tomorrow', inDays: 'Due in {n} days' },
  upload: {
    title: 'New correspondence', subtitle: 'The AI will summarize it, classify it and suggest a department.',
    fileTab: 'Upload file', textTab: 'Paste text',
    drop: 'Drop a file here, or click to browse', types: 'PDF · DOCX · JPG · PNG · EML · TXT — up to 20 MB',
    textPlaceholder: 'Paste the letter or email text here…', success: 'Uploaded. AI analysis has started.', change: 'Choose a different file',
  },
  detail: {
    original: 'Original document', aiAnalysis: 'AI analysis', summary: 'Summary', fields: 'Key details',
    sender: 'Sender', recipient: 'Recipient', reference: 'Reference', date: 'Dated', deadline: 'Deadline',
    language: 'Language', actionItems: 'Action items', noActionItems: 'No action required.',
    suggestion: 'Suggested department', confidence: 'confidence', reasoning: 'Why this department',
    lowConfidence: 'The AI is unsure about this one. Please choose the department.',
    noSuggestion: 'The AI could not suggest a department.', routedTo: 'Routed to', overridden: 'Overrode the AI suggestion',
    acceptedAi: 'Accepted the AI suggestion', note: 'Add a note for the department (optional)', processing: 'Reading the document…',
    processingHint: 'Summaries, key details and a routing suggestion will appear here in a few seconds.',
    extractedText: 'Text', file: 'File', noPreview: 'No preview available for this file type.', model: 'Analyzed by', tokens: 'tokens',
    due: 'Due', received: 'Received', decision: 'Routing decision', reroute: 'Change routing', failed: 'Analysis failed',
  },
  dashboard: {
    title: 'Insights', subtitle: 'How the inbox and the AI are performing.',
    total: 'Documents', needsReview: 'Needs review', routed: 'Routed', accuracy: 'AI routing accuracy',
    accuracyHint: '{accepted} of {total} suggestions accepted without change', accuracyEmpty: 'Route a few documents to measure accuracy.',
    avgConfidence: 'Average confidence', tokens: 'Tokens used', byPriority: 'By priority', byCategory: 'By category',
    byDepartment: 'Routed by department', byStatus: 'By status', empty: 'No data yet. Upload some documents to get started.',
  },
  empty: {
    inboxTitle: 'Your inbox is empty', inbox: 'Upload a letter, scan or email and watch the AI sort it.',
    noResultsTitle: 'Nothing matches', noResults: 'Try another filter or search term.',
  },
  errors: { generic: 'Something went wrong' },
  engine: { rules: 'Rule engine', local: 'Local AI', cloud: 'Claude (cloud)', free: 'no cost' },
  handling: {
    New: 'New', Acknowledged: 'In progress', Closed: 'Closed',
    section: 'Departments', inbox: 'Department inbox', subtitle: 'Correspondence routed to this department.',
    all: 'All', overdue: '{n} overdue',
    acknowledge: 'Acknowledge', close: 'Close', reopen: 'Reopen',
    closeTitle: 'Close this correspondence', closeNote: 'What was done? (optional)',
    acknowledged: 'Acknowledged', closed: 'Closed', reopened: 'Reopened',
    stateLabel: 'Department status', closedNote: 'Closing note', empty: 'Nothing here.',
    emptyNew: 'No new correspondence. You are all caught up.', openInbox: 'Open department inbox',
  },
  notify: {
    sent: 'Email sent to {to}', pending: 'Email to {to} is queued', failed: 'Email to {to} failed',
    retrying: 'attempt {n}, will retry',
    closedSent: '{to} was told it is closed', closedPending: 'Telling {to} it is closed…', closedFailed: 'Could not email {to}',
  },
  auth: {
    title: 'Sign in', subtitle: 'Sign in to your correspondence workspace.',
    email: 'Email', password: 'Password', signIn: 'Sign in', or: 'or', microsoft: 'Sign in with Microsoft',
    signOut: 'Sign out', failed: 'Sign-in failed', entraError: 'Microsoft sign-in did not complete. Please try again.',
    locked: 'This account is locked. Contact your administrator.',
    pendingTitle: 'Almost there', pendingText: 'Your account was created, but an administrator still needs to give you access (a role and, for department staff, a department). Please contact your administrator, then sign in again.',
    roles: { Admin: 'Administrator', Clerk: 'Correspondence desk', Department: 'Department staff', none: 'No access yet' },
    demo: 'Demo accounts',
  },
  admin: {
    title: 'Administration', subtitle: 'People, roles and department email addresses.', nav: 'Administration',
    users: 'Users', departments: 'Department emails', add: 'Add user', name: 'Name', role: 'Role', department: 'Department',
    signIn: 'Sign-in', lastSeen: 'Last sign-in', never: 'Never', password: 'Password', passwordHint: 'Leave empty for Microsoft-only sign-in',
    disabled: 'Disabled', active: 'Active', save: 'Save', saved: 'Saved', edit: 'Edit', setPassword: 'Set password',
    newPassword: 'New password', pending: 'Needs a role', local: 'Password', emailPlaceholder: 'No email (notifications off)',
    deptEmail: 'Department inbox', deptEmailHint: 'Routing emails and daily digest',
    headEmail: 'Department head', headEmailHint: 'Escalations for overdue items', headPlaceholder: 'Desk mailbox (fallback)',
  },
}

const ar: typeof en = {
  app: { title: 'البريد الذكي', subtitle: 'وزارة الخدمات الرقمية – تجريبي', live: 'التحديث المباشر يعمل', offline: 'جارٍ إعادة الاتصال…' },
  nav: { inbox: 'الوارد', needsReview: 'بحاجة إلى مراجعة', ready: 'جاهز للإحالة', dashboard: 'المؤشرات', theme: 'المظهر', language: 'اللغة', dark: 'داكن', light: 'فاتح' },
  actions: {
    upload: 'مراسلة جديدة', reanalyze: 'إعادة التحليل', accept: 'اعتماد وإحالة', override: 'اختيار إدارة أخرى',
    route: 'إحالة', cancel: 'إلغاء', back: 'الوارد', clear: 'مسح', open: 'فتح', retry: 'إعادة المحاولة',
  },
  inbox: {
    title: 'الوارد', subtitle: 'يقرأ الذكاء الاصطناعي كل مستند ويقترح جهة الإحالة، والقرار لك.',
    all: 'الكل', results: '{n} مستند', results1: 'مستند واحد', waiting: 'بانتظار الإدارة',
  },
  timeline: {
    title: 'سجل النشاط', system: 'النظام',
    Uploaded: 'ورد المستند', Analyzed: 'تم التحليل، واقتُرحت {dept}', AnalysisFailed: 'فشل التحليل',
    Routed: 'أُحيل إلى {dept}', Acknowledged: 'استلمته {dept}', Closed: 'أغلقته {dept}',
    Reopened: 'أعادت {dept} فتحه', EmailSent: 'أُرسل بريد إلكتروني', EmailFailed: 'تعذّر إرسال البريد',
    Escalated: 'متأخر: صُعّد إلى مدير {dept}',
  },
  filters: { search: 'ابحث بالمرسل أو الرقم المرجعي أو المحتوى…', status: 'الحالة', priority: 'الأولوية', department: 'الإدارة' },
  status: { Uploaded: 'تم الرفع', Processing: 'قيد التحليل', Analyzed: 'جاهز للإحالة', Routed: 'تمت الإحالة', NeedsReview: 'بحاجة إلى مراجعة', Failed: 'فشل' },
  priority: { Low: 'منخفضة', Normal: 'عادية', High: 'مرتفعة', Urgent: 'عاجلة' },
  category: { Request: 'طلب', Complaint: 'شكوى', Invitation: 'دعوة', Circular: 'تعميم', Invoice: 'فاتورة', Legal: 'قانوني', Other: 'أخرى' },
  deadline: { overdue: 'متأخر {n} يوم', today: 'مستحق اليوم', tomorrow: 'مستحق غدًا', inDays: 'مستحق خلال {n} أيام' },
  upload: {
    title: 'مراسلة جديدة', subtitle: 'سيلخصها الذكاء الاصطناعي ويصنفها ويقترح الإدارة المختصة.',
    fileTab: 'رفع ملف', textTab: 'لصق نص',
    drop: 'أفلت الملف هنا أو انقر للاختيار', types: 'PDF · DOCX · JPG · PNG · EML · TXT — حتى 20 ميغابايت',
    textPlaceholder: 'الصق نص الخطاب أو البريد الإلكتروني هنا…', success: 'تم الرفع وبدأ التحليل الذكي.', change: 'اختيار ملف آخر',
  },
  detail: {
    original: 'المستند الأصلي', aiAnalysis: 'تحليل الذكاء الاصطناعي', summary: 'الملخص', fields: 'البيانات الرئيسية',
    sender: 'المرسل', recipient: 'المرسل إليه', reference: 'الرقم المرجعي', date: 'التاريخ', deadline: 'الموعد النهائي',
    language: 'اللغة', actionItems: 'الإجراءات المطلوبة', noActionItems: 'لا توجد إجراءات مطلوبة.',
    suggestion: 'الإدارة المقترحة', confidence: 'ثقة', reasoning: 'سبب الاقتراح',
    lowConfidence: 'الذكاء الاصطناعي غير متأكد من هذه المراسلة. يرجى اختيار الإدارة.',
    noSuggestion: 'لم يتمكن الذكاء الاصطناعي من اقتراح إدارة.', routedTo: 'أُحيلت إلى', overridden: 'تم تعديل اقتراح الذكاء الاصطناعي',
    acceptedAi: 'تم اعتماد اقتراح الذكاء الاصطناعي', note: 'أضف ملاحظة للإدارة (اختياري)', processing: 'جارٍ قراءة المستند…',
    processingHint: 'سيظهر الملخص والبيانات الرئيسية والإحالة المقترحة هنا خلال ثوانٍ.',
    extractedText: 'النص', file: 'الملف', noPreview: 'لا تتوفر معاينة لهذا النوع من الملفات.', model: 'حلّله', tokens: 'رمز',
    due: 'الاستحقاق', received: 'ورد', decision: 'قرار الإحالة', reroute: 'تغيير الإحالة', failed: 'فشل التحليل',
  },
  dashboard: {
    title: 'المؤشرات', subtitle: 'أداء صندوق الوارد والذكاء الاصطناعي.',
    total: 'المستندات', needsReview: 'بحاجة إلى مراجعة', routed: 'تمت إحالتها', accuracy: 'دقة الإحالة الذكية',
    accuracyHint: 'اعتُمد {accepted} من {total} اقتراحًا دون تعديل', accuracyEmpty: 'أحِل بعض المستندات لقياس الدقة.',
    avgConfidence: 'متوسط الثقة', tokens: 'الرموز المستخدمة', byPriority: 'حسب الأولوية', byCategory: 'حسب التصنيف',
    byDepartment: 'الإحالات حسب الإدارة', byStatus: 'حسب الحالة', empty: 'لا توجد بيانات بعد. ارفع بعض المستندات للبدء.',
  },
  empty: {
    inboxTitle: 'صندوق الوارد فارغ', inbox: 'ارفع خطابًا أو مستندًا ممسوحًا أو بريدًا إلكترونيًا وشاهد الذكاء الاصطناعي يفرزه.',
    noResultsTitle: 'لا توجد نتائج', noResults: 'جرّب عامل تصفية أو كلمة بحث أخرى.',
  },
  errors: { generic: 'حدث خطأ ما' },
  engine: { rules: 'محرك القواعد', local: 'ذكاء اصطناعي محلي', cloud: 'Claude (سحابي)', free: 'بدون تكلفة' },
  handling: {
    New: 'جديدة', Acknowledged: 'قيد الإنجاز', Closed: 'مغلقة',
    section: 'الإدارات', inbox: 'وارد الإدارة', subtitle: 'المراسلات المحالة إلى هذه الإدارة.',
    all: 'الكل', overdue: '{n} متأخرة',
    acknowledge: 'استلام', close: 'إغلاق', reopen: 'إعادة فتح',
    closeTitle: 'إغلاق المراسلة', closeNote: 'ما الإجراء الذي تم؟ (اختياري)',
    acknowledged: 'تم الاستلام', closed: 'تم الإغلاق', reopened: 'أعيد فتحها',
    stateLabel: 'حالة الإدارة', closedNote: 'ملاحظة الإغلاق', empty: 'لا يوجد شيء هنا.',
    emptyNew: 'لا توجد مراسلات جديدة. أنجزت كل شيء.', openInbox: 'فتح وارد الإدارة',
  },
  notify: {
    sent: 'أُرسل بريد إلى {to}', pending: 'البريد إلى {to} في الانتظار', failed: 'فشل إرسال البريد إلى {to}',
    retrying: 'المحاولة {n}، ستتم إعادة المحاولة',
    closedSent: 'تم إبلاغ {to} بالإغلاق', closedPending: 'جارٍ إبلاغ {to} بالإغلاق…', closedFailed: 'تعذّر إبلاغ {to}',
  },
  auth: {
    title: 'تسجيل الدخول', subtitle: 'سجّل الدخول إلى مساحة عمل المراسلات.',
    email: 'البريد الإلكتروني', password: 'كلمة المرور', signIn: 'دخول', or: 'أو', microsoft: 'الدخول بحساب Microsoft',
    signOut: 'تسجيل الخروج', failed: 'تعذّر تسجيل الدخول', entraError: 'لم يكتمل الدخول عبر Microsoft. حاول مرة أخرى.',
    locked: 'هذا الحساب مقفل. تواصل مع مسؤول النظام.',
    pendingTitle: 'خطوة أخيرة', pendingText: 'تم إنشاء حسابك، لكن يجب على مسؤول النظام منحك الصلاحية (دور، وإدارة لموظفي الإدارات). تواصل مع المسؤول ثم سجّل الدخول مرة أخرى.',
    roles: { Admin: 'مسؤول النظام', Clerk: 'مكتب المراسلات', Department: 'موظف إدارة', none: 'بلا صلاحية بعد' },
    demo: 'حسابات تجريبية',
  },
  admin: {
    title: 'الإدارة', subtitle: 'المستخدمون والأدوار وعناوين بريد الإدارات.', nav: 'الإدارة',
    users: 'المستخدمون', departments: 'بريد الإدارات', add: 'إضافة مستخدم', name: 'الاسم', role: 'الدور', department: 'الإدارة',
    signIn: 'طريقة الدخول', lastSeen: 'آخر دخول', never: 'لم يدخل', password: 'كلمة المرور', passwordHint: 'اتركها فارغة للدخول عبر Microsoft فقط',
    disabled: 'معطّل', active: 'نشط', save: 'حفظ', saved: 'تم الحفظ', edit: 'تعديل', setPassword: 'تعيين كلمة مرور',
    newPassword: 'كلمة مرور جديدة', pending: 'بحاجة إلى دور', local: 'كلمة مرور', emailPlaceholder: 'بلا بريد (الإشعارات متوقفة)',
    deptEmail: 'بريد الإدارة', deptEmailHint: 'إشعارات الإحالة والملخص اليومي',
    headEmail: 'مدير الإدارة', headEmailHint: 'التصعيد عند تجاوز الموعد النهائي', headPlaceholder: 'بريد مكتب المراسلات (احتياطي)',
  },
}

export type Locale = 'en' | 'ar'

function initialLocale(): Locale {
  try {
    const saved = localStorage.getItem('locale')
    if (saved === 'en' || saved === 'ar') return saved
  } catch {
    /* storage unavailable */
  }
  return navigator.language.startsWith('ar') ? 'ar' : 'en'
}

export const i18n = createI18n({
  legacy: false,
  locale: initialLocale(),
  fallbackLocale: 'en',
  messages: { en, ar },
})

export function applyLocale(locale: Locale) {
  i18n.global.locale.value = locale
  document.documentElement.lang = locale
  document.documentElement.dir = locale === 'ar' ? 'rtl' : 'ltr'
  try {
    localStorage.setItem('locale', locale)
  } catch {
    /* storage unavailable */
  }
}
