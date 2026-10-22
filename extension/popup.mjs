const strings = {
  title:["ترجمة الصفحة","Translate page","Traduire la page","Traducir página"],
  description:["استبدال نصوص الصفحة المفتوحة مع إمكانية استعادة الأصل.","Replace text on the active page and restore it anytime.","Remplacez le texte de la page et restaurez-le à tout moment.","Reemplaza el texto de la página y restáuralo cuando quieras."],
  key:["مفتاح Gemini لهذه الجلسة","Gemini key for this session","Clé Gemini pour cette session","Clave Gemini para esta sesión"],
  model:["معرّف نموذج Gemini","Gemini model ID","Identifiant du modèle Gemini","ID del modelo Gemini"],
  target:["اللغة المطلوبة","Target language","Langue cible","Idioma de destino"],
  translate:["ترجمة الصفحة","Translate page","Traduire la page","Traducir página"],
  restore:["استعادة النص الأصلي","Restore original","Restaurer l’original","Restaurar original"],
  forget:["حذف المفتاح","Forget key","Supprimer la clé","Eliminar clave"],
  privacy:["بالضغط على الترجمة تُرسل نصوص الصفحة إلى Google. لا تُرسل حقول الإدخال. يبقى المفتاح في ذاكرة جلسة المتصفح. حتى 300 مقطع في الطلب؛ الصور والإطارات الداخلية غير مشمولة.","Clicking Translate sends page text to Google. Input fields are excluded. Key stays in browser session memory. Up to 300 text segments per run; images and frames excluded.","Traduire envoie le texte à Google, hors champs de saisie. Clé en mémoire de session. Jusqu’à 300 segments ; images et cadres exclus.","Traducir envía texto a Google, sin campos de entrada. Clave en memoria de sesión. Hasta 300 segmentos; sin imágenes ni marcos."],
  working:["جارٍ الترجمة… يمكنك إبقاء هذه النافذة مفتوحة","Translating… keep this popup open","Traduction… gardez cette fenêtre ouverte","Traduciendo… mantén esta ventana abierta"],
  done:["اكتملت العملية","Completed","Terminé","Completado"]
};
const $=id=>document.getElementById(id);
let lang="ar";
function t(key){return strings[key][["ar","en","fr","es"].indexOf(lang)]}
function render(){document.documentElement.lang=lang;document.documentElement.dir=lang==="ar"?"rtl":"ltr";document.querySelectorAll("[data-i]").forEach(el=>el.textContent=t(el.dataset.i));}
const config=await chrome.storage.local.get(["language","model","target"]);
lang=config.language??"ar";$("language").value=lang;$("model").value=config.model??"";$("target").value=config.target??"Arabic";
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
