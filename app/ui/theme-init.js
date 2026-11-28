(function () {
  var params = new URLSearchParams(window.location.search);
  var token = params.get("token");
  if (token) {
    try { sessionStorage.setItem("qz_thumbs_token", token); } catch (_) {}
  }
  var theme = params.get("theme");
  if (theme !== "dark" && theme !== "light") {
    try { theme = localStorage.getItem("qz_thumbs_theme") || "dark"; } catch (_) { theme = "dark"; }
  }
  var lang = params.get("lang");
  if (lang !== "ar" && lang !== "en") {
    try { lang = localStorage.getItem("qz_thumbs_lang") || "ar"; } catch (_) { lang = "ar"; }
  }
  document.documentElement.dataset.theme = theme;
  document.documentElement.lang = lang;
  document.documentElement.dir = lang === "ar" ? "rtl" : "ltr";
  document.documentElement.style.backgroundColor = theme === "light" ? "#F7F6F2" : "#0C0C0E";
})();
