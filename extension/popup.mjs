import {defaultLocale, locales} from "./protocol.mjs";

const strings = {
  title:["ترجمة الصفحة","Translate page","Traduire la page","Traducir página","翻译网页","翻譯網頁","Dịch trang"],
  description:["استبدال نصوص الصفحة المفتوحة مع إمكانية استعادة الأصل.","Replace text on the active page and restore it anytime.","Remplacez le texte de la page et restaurez-le à tout moment.","Reemplaza el texto de la página y restáuralo cuando quieras.","替换当前网页的文字，随时可以恢复原文。","替換目前網頁的文字，隨時可還原原文。","Thay văn bản trên trang đang mở và khôi phục bản gốc bất cứ lúc nào."],
  key:["مفتاح Gemini لهذه الجلسة","Gemini key for this session","Clé Gemini pour cette session","Clave Gemini para esta sesión","本次会话的 Gemini 密钥","此工作階段的 Gemini 金鑰","Khóa Gemini cho phiên này"],
  model:["معرّف نموذج Gemini","Gemini model ID","Identifiant du modèle Gemini","ID del modelo Gemini","Gemini 模型 ID","Gemini 模型 ID","Mã mô hình Gemini"],
  target:["اللغة المطلوبة","Target language","Langue cible","Idioma de destino","目标语言","目標語言","Ngôn ngữ đích"],
  restore:["استعادة النص الأصلي","Restore original","Restaurer l’original","Restaurar el original","恢复原文","還原原文","Khôi phục bản gốc"],
  forget:["حذف المفتاح","Forget key","Supprimer la clé","Eliminar clave","清除密钥","清除金鑰","Xóa khóa"],
  privacy:["بالضغط على الترجمة تُرسل نصوص الصفحة إلى Google. لا تُرسل حقول الإدخال. يبقى المفتاح في ذاكرة جلسة المتصفح. حتى 300 مقطع في الطلب؛ الصور والإطارات الداخلية غير مشمولة.","Clicking Translate sends page text to Google. Input fields, images and frames are excluded. The key stays in browser session memory. Each run can translate up to 300 text segments.","La traduction envoie le texte de la page à Google. Les champs de saisie, images et cadres sont exclus. La clé reste en mémoire pour la session du navigateur. Chaque demande traite jusqu’à 300 segments.","Al traducir, se envía el texto de la página a Google. Se excluyen los campos de entrada, las imágenes y los marcos. La clave queda en la memoria de la sesión. Cada traducción admite hasta 300 segmentos.","开始翻译会向 Google 发送网页文字，不包含输入框、图片和框架内容。密钥仅保留在浏览器会话中。每次最多翻译 300 段。","開始翻譯會將網頁文字傳給 Google，不包含輸入欄、圖片或頁框。金鑰僅保留在瀏覽器工作階段中。每次最多翻譯 300 段。","Khi dịch, văn bản trên trang được gửi đến Google. Không gửi nội dung trong ô nhập liệu, ảnh hoặc khung. Khóa chỉ lưu trong phiên trình duyệt. Mỗi lượt dịch tối đa 300 đoạn văn bản."],
  working:["جارٍ الترجمة… يمكنك إبقاء هذه النافذة مفتوحة","Translating… keep this popup open","Traduction… gardez cette fenêtre ouverte","Traduciendo… mantén esta ventana abierta","正在翻译…请保持此窗口打开","正在翻譯…請保持此視窗開啟","Đang dịch… Hãy giữ cửa sổ này mở"],
  done:["اكتملت العملية","Completed","Terminé","Completado","已完成","已完成","Đã hoàn tất"],
  settings:["الإعدادات","Settings","Paramètres","Configuración","设置","設定","Cài đặt"],
  language:["لغة الواجهة","Interface language","Langue de l’interface","Idioma de la interfaz","界面语言","介面語言","Ngôn ngữ giao diện"]
};
const $=id=>document.getElementById(id);
let lang=defaultLocale.code;
function t(key){return strings[key][locales.findIndex(locale => locale.code === lang)]}
function render(){document.documentElement.lang=lang;document.documentElement.dir=lang==="ar"?"rtl":"ltr";document.querySelectorAll("[data-i]").forEach(el=>el.textContent=t(el.dataset.i));}
const config=await chrome.storage.local.get(["language","model","target"]);
for (const locale of [defaultLocale, ...locales.filter(locale => locale !== defaultLocale)]) {
  $("language").add(new Option(locale.label, locale.code));
  $("target").add(new Option(locale.label, locale.target));
}
lang=locales.some(locale => locale.code === config.language) ? config.language : defaultLocale.code;
$("language").value=lang;
$("model").value=config.model??"";
$("target").value=locales.some(locale => locale.target === config.target) ? config.target : defaultLocale.target;
const session=await chrome.storage.session.get("key");$("key").value=session.key??"";render();
$("language").onchange=async()=>{lang=$("language").value;await chrome.storage.local.set({language:lang});render()};
$("forget").onclick=async()=>{await chrome.storage.session.remove("key");$("key").value="";$("status").textContent=t("done")};
async function run(action){
  $("translate").disabled=true;$("restore").disabled=true;$("status").textContent=t("working");
  try{
    await chrome.storage.session.set({key:$("key").value.trim()});
    await chrome.storage.local.set({model:$("model").value.trim(),target:$("target").value});
    const result=await chrome.runtime.sendMessage({action});
    $("status").textContent=result.ok?t("done")+(result.count!==undefined?` · ${result.count}`:""):result.error;
  }catch(error){$("status").textContent=error.message}
  finally{$("translate").disabled=false;$("restore").disabled=false}
}
$("translate").onclick=()=>run("translate");$("restore").onclick=()=>run("restore");
