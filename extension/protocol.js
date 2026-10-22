export function validateTranslations(value, originals) {
  if (!Array.isArray(value) || value.length !== originals.length) throw new Error("Invalid translation count");
  const seen = new Set();
  for (const item of value) {
    if (!Number.isInteger(item.id) || item.id < 0 || item.id >= originals.length || seen.has(item.id) ||
      typeof item.text !== "string" || item.text.length > Math.max(2000, originals[item.id].length * 6)) {
      throw new Error("Invalid translation item");
    }
    seen.add(item.id);
  }
  return value.sort((a,b) => a.id-b.id).map(item => item.text);
}
