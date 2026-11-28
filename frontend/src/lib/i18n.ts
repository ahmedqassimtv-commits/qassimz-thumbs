export type Lang = "ar" | "en";
export type Theme = "dark" | "light";

export interface Copy {
  appBrandAr: string;
  appSubAr: string;
  appBrandEn: string;
  appSubEn: string;
  tabFormats: string;
  tabViewer: string;
  statusLinked: string;
  statusNeedsActivation: string;
  statusSubtitleLinked: (active: number, total: number) => string;
  statusSubtitleNeeds: string;
  enableAll: string;
  disableAll: string;
  rebuildCache: string;
  rebuilding: string;
  groupDesign: string;
  groupEditing: string;
  groupRawModern: string;
  badgeActive: string;
  badgeOff: string;
  registryRootLabel: string;
  dllFoundLabel: string;
  dllMissingLabel: string;
  dropTitle: string;
  dropSubtitle: string;
  dropBrowse: string;
  dropHint: string;
  clearPreview: string;
  metaDimensions: string;
  metaSize: string;
  metaFormat: string;
  metaTime: string;
  metaAlpha: string;
  alphaYes: string;
  alphaNo: string;
  extracting: string;
  splashStages: [string, string, string];
  splashSuite: string;
  splashCopyright: string;
  versionLabel: string;
}

const AR: Copy = {
  appBrandAr: "قَاسِمز",
  appSubAr: "المُصغّرات",
  appBrandEn: "QASSIMZ",
  appSubEn: "Thumbs",
  tabFormats: "صيغ المصغرات",
  tabViewer: "العارض الفوري",
  statusLinked: "يعمل ومرتبط بالنظام ✓",
  statusNeedsActivation: "يحتاج تفعيل",
  statusSubtitleLinked: (active, total) =>
    `${active} من ${total} مجموعات صيغ مسجّلة فعلياً في مستكشف ويندوز (HKCU بدون صلاحيات مسؤول)`,
  statusSubtitleNeeds:
    "فعّل الصيغ أدناه لتسجيل مزوّد المصغرات مباشرة في مستكشف ويندوز بدون UAC",
  enableAll: "تفعيل الكل",
  disableAll: "إيقاف الكل",
  rebuildCache: "تحديث كاش ويندوز",
  rebuilding: "جارٍ تحديث الكاش…",
  groupDesign: "التصميم والفيكتور",
  groupEditing: "المونتاج والموشن",
  groupRawModern: "صور خام وحديثة",
  badgeActive: "مفعّل ✓",
  badgeOff: "متوقف",
  registryRootLabel: "مسار التسجيل",
  dllFoundLabel: "مزوّد COM جاهز (64-bit)",
  dllMissingLabel: "ملف DLL غير موجود",
  dropTitle: "اسحب وأفلت أي ملف تصميم أو صورة هنا",
  dropSubtitle: "PSD · PSB · AI · EPS · MOGRT · RAW · SVG · WEBP · HEIC · PNG",
  dropBrowse: "أو اختر ملفاً من جهازك",
  dropHint: "استخراج فوري للمعاينة المدمجة مع إظهار الشفافية ووقت المعالجة بالملي ثانية",
  clearPreview: "ملف آخر",
  metaDimensions: "الأبعاد",
  metaSize: "الحجم",
  metaFormat: "الصيغة",
  metaTime: "زمن الاستخراج",
  metaAlpha: "الشفافية",
  alphaYes: "شفاف (Alpha)",
  alphaNo: "معتم (RGB)",
  extracting: "جارٍ استخراج المعاينة المدمجة…",
  splashStages: [
    "جارٍ تهيئة محرك المصغّرات…",
    "فحص الصيغ المسجّلة في النظام…",
    "النظام جاهز تقريباً…",
  ],
  splashSuite: "منظومة قاسمز الإبداعية",
  splashCopyright: "© 2026 QASSIMZ. All rights reserved. • Version 1.4.0",
  versionLabel: "الإصدار 1.4.0",
};

const EN: Copy = {
  appBrandAr: "قَاسِمز",
  appSubAr: "المُصغّرات",
  appBrandEn: "QASSIMZ",
  appSubEn: "Thumbs",
  tabFormats: "Shell Formats",
  tabViewer: "Quick Viewer",
  statusLinked: "Active & Linked to Explorer ✓",
  statusNeedsActivation: "Needs Activation",
  statusSubtitleLinked: (active, total) =>
    `${active} of ${total} format groups registered in Windows Explorer (HKCU, zero UAC)`,
  statusSubtitleNeeds:
    "Enable formats below to register the 64-bit IThumbnailProvider directly in HKCU",
  enableAll: "Enable All",
  disableAll: "Disable All",
  rebuildCache: "Rebuild Windows Cache",
  rebuilding: "Rebuilding cache…",
  groupDesign: "Design & Vector",
  groupEditing: "Motion & Editing",
  groupRawModern: "RAW & Modern Imaging",
  badgeActive: "Active ✓",
  badgeOff: "Disabled",
  registryRootLabel: "Registry Root",
  dllFoundLabel: "64-bit COM Provider Ready",
  dllMissingLabel: "Shell DLL Missing",
  dropTitle: "Drag & drop any design or RAW file here",
  dropSubtitle: "PSD · PSB · AI · EPS · MOGRT · RAW · SVG · WEBP · HEIC · PNG",
  dropBrowse: "Or choose a file",
  dropHint: "Instant embedded-preview extraction with alpha checkerboard & latency telemetry",
  clearPreview: "Inspect another file",
  metaDimensions: "Dimensions",
  metaSize: "File Size",
  metaFormat: "Format",
  metaTime: "Extraction",
  metaAlpha: "Alpha",
  alphaYes: "Transparent",
  alphaNo: "Opaque",
  extracting: "Extracting embedded preview…",
  splashStages: [
    "Preparing thumbnail engine…",
    "Checking registered formats…",
    "Almost ready…",
  ],
  splashSuite: "QASSIMZ Creative Suite",
  splashCopyright: "© 2026 QASSIMZ. All rights reserved. • Version 1.4.0",
  versionLabel: "Version 1.4.0",
};

export function getCopy(lang: Lang): Copy {
  return lang === "ar" ? AR : EN;
}
