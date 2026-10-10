const adminTokenStorageKey = "rackloader-publisher-admin-token";
const languageStorageKey = "rackloader-publisher-language";
const activationCode = new URLSearchParams(window.location.search).get("activate");
const adminLoginView = document.getElementById("admin-login-view");
const adminLoginForm = document.getElementById("admin-login-form");
const adminTokenInput = document.getElementById("admin-token");
const adminArea = document.getElementById("admin-area");
const activationView = document.getElementById("activation-view");
const adminLoginMessage = document.getElementById("admin-message-login");
const translations = {
  en: {
    "document.title": "RackLoader Publisher",
    "meta.description": "Manage trusted Rack Mod publishers and issue secure activation invites.",
    "language.label": "Language",
    "header.network": "Signing & trust",
    "header.online": "ONLINE",
    "header.offline": "OFFLINE",
    "hero.admin.eyebrow": "PUBLISHER CONTROL",
    "hero.admin.title": "Trust<br><em>starts here.</em>",
    "hero.admin.description": "The Rack Mod publisher console. Sign in with your administrator token to issue invitations and manage trusted publishers.",
    "hero.admin.points": "<span><b>01</b> Issue publisher invitations</span><span><b>02</b> Manage signing keys</span><span><b>03</b> Maintain trusted publishers</span>",
    "hero.activation.eyebrow": "PUBLISHER ACTIVATION",
    "hero.activation.title": "Your key<br><em>is ready.</em>",
    "hero.activation.description": "Your Rack Mod invitation is ready. Activate your publisher key to receive a private token for the mod packer.",
    "hero.activation.points": "<span><b>01</b> Activate in one click</span><span><b>02</b> Token is shown once</span><span><b>03</b> Private key stays protected</span>",
    "login.eyebrow": "ADMINISTRATOR ACCESS",
    "login.title": "Sign in to the control panel",
    "login.caption": "Enter the administrator token from your local deployment file.",
    "login.tokenLabel": "ADMINISTRATOR TOKEN",
    "login.tokenPlaceholder": "Paste your administrator token",
    "login.submit": "Sign in",
    "admin.eyebrow": "PUBLISHER MANAGEMENT",
    "admin.title": "Trust center",
    "admin.logout": "Sign out",
    "invite.title": "Create an invitation",
    "invite.description": "The link can be activated once to create a publisher signing key.",
    "invite.step": "ISSUER",
    "invite.nameLabel": "PUBLISHER LABEL",
    "invite.namePlaceholder": "Team / developer",
    "invite.modLabel": "MOD ID",
    "common.optional": "OPTIONAL",
    "invite.modPlaceholder": "e.g. com.rackworks.map",
    "invite.expiryLabel": "EXPIRES IN",
    "invite.days7": "7 days",
    "invite.day1": "1 day",
    "invite.days30": "30 days",
    "invite.create": "Create invite link",
    "invite.resultTitle": "Activation link created",
    "invite.oneTime": "ONE TIME ONLY",
    "invite.urlLabel": "Developer activation link",
    "invite.expiresLabel": "Expires",
    "invite.note": "Send this link to the developer. They can activate it with one click; only they will see the publishing token, and only once.",
    "copy.button": "COPY",
    "copy.done": "COPIED",
    "directory.title": "Publisher registry",
    "directory.description": "Active public keys are trusted by RackLoader.",
    "directory.refresh": "Refresh list",
    "directory.loading": "Loading registry…",
    "directory.empty": "No publishers yet. Create an activation link above to get started.",
    "directory.anyMod": "Any Mod ID",
    "directory.added": "Added",
    "directory.active": "ACTIVE",
    "directory.revoked": "REVOKED",
    "directory.revoke": "Revoke",
    "directory.revokeConfirm": "Revoke this publisher key? New signatures will stop, and clients will remove trust the next time they refresh the key list.",
    "activation.ready": "INVITATION READY",
    "activation.title": "Activate your publisher key",
    "activation.description": "This invitation is assigned to you. Activate it to create a key for signing mods. Your publishing token will only be shown once.",
    "activation.button": "Activate key",
    "activation.once": "ONE-TIME ACTIVATION",
    "activation.invalid": "This activation link is invalid, incomplete, expired, or has already been used.",
    "activation.creating": "Creating your publisher key…",
    "activation.keyActive": "KEY ACTIVE",
    "activation.successTitle": "Your key is active",
    "activation.successDescription": "Save your publishing token now. After you close this page, it cannot be viewed again.",
    "activation.tokenTitle": "Developer token",
    "activation.tokenOnce": "SHOWN ONCE",
    "activation.publisher": "PUBLISHER",
    "activation.keyId": "KEY ID",
    "activation.modId": "ALLOWED MOD ID",
    "activation.anyMod": "Any Mod ID",
    "activation.packerHint": "ADD TO CI AS RACKLOADER_DEVELOPER_KEY",
    "footer.format": "RACK MOD FORMAT <b>01</b>",
    "error.unauthorized": "The administrator token is invalid or the session has expired.",
    "error.invalidInvite": "This activation link is invalid, expired, or has already been used.",
    "error.developerMissing": "This active publisher could not be found.",
    "error.requestFailed": "The request could not be completed. Check the entered details and try again.",
    "error.serviceUnavailable": "The service could not complete the request. Please try again later.",
    "error.clipboard": "Could not copy automatically. Select and copy the value manually."
  },
  ru: {
    "document.title": "RackLoader Publisher",
    "meta.description": "Управление доверенными издателями Rack Mod и создание защищённых ссылок активации.",
    "language.label": "Язык",
    "header.network": "Подпись и доверие",
    "header.online": "В СЕТИ",
    "header.offline": "НЕ В СЕТИ",
    "hero.admin.eyebrow": "ПАНЕЛЬ ИЗДАТЕЛЯ",
    "hero.admin.title": "Доверие<br><em>начинается здесь.</em>",
    "hero.admin.description": "Панель издателей Rack Mod. Войдите с токеном администратора, чтобы выдавать приглашения и управлять доверенными издателями.",
    "hero.admin.points": "<span><b>01</b> Выдача приглашений издателям</span><span><b>02</b> Управление ключами подписи</span><span><b>03</b> Список доверенных издателей</span>",
    "hero.activation.eyebrow": "АКТИВАЦИЯ ИЗДАТЕЛЯ",
    "hero.activation.title": "Ваш ключ<br><em>готов.</em>",
    "hero.activation.description": "Приглашение Rack Mod готово. Активируйте ключ издателя, чтобы получить личный токен для упаковщика модов.",
    "hero.activation.points": "<span><b>01</b> Активация одним нажатием</span><span><b>02</b> Токен показывается один раз</span><span><b>03</b> Закрытый ключ защищён</span>",
    "login.eyebrow": "ДОСТУП АДМИНИСТРАТОРА",
    "login.title": "Вход в панель управления",
    "login.caption": "Введите токен администратора из локального файла деплоя.",
    "login.tokenLabel": "ТОКЕН АДМИНИСТРАТОРА",
    "login.tokenPlaceholder": "Вставьте токен администратора",
    "login.submit": "Войти",
    "admin.eyebrow": "УПРАВЛЕНИЕ ИЗДАТЕЛЯМИ",
    "admin.title": "Центр доверия",
    "admin.logout": "Выйти",
    "invite.title": "Новое приглашение",
    "invite.description": "Ссылку можно активировать один раз, чтобы создать ключ подписи издателя.",
    "invite.step": "ИЗДАТЕЛЬ",
    "invite.nameLabel": "ИМЯ ИЗДАТЕЛЯ",
    "invite.namePlaceholder": "Команда / разработчик",
    "invite.modLabel": "ID МОДА",
    "common.optional": "НЕОБЯЗАТЕЛЬНО",
    "invite.modPlaceholder": "Например, com.rackworks.map",
    "invite.expiryLabel": "СРОК ДЕЙСТВИЯ",
    "invite.days7": "7 дней",
    "invite.day1": "1 день",
    "invite.days30": "30 дней",
    "invite.create": "Создать ссылку",
    "invite.resultTitle": "Ссылка активации создана",
    "invite.oneTime": "ОДНОРАЗОВАЯ",
    "invite.urlLabel": "Ссылка для разработчика",
    "invite.expiresLabel": "Действует до",
    "invite.note": "Передайте ссылку разработчику. Он активирует её одним нажатием; токен публикации будет показан только ему и только один раз.",
    "copy.button": "КОПИРОВАТЬ",
    "copy.done": "СКОПИРОВАНО",
    "directory.title": "Реестр издателей",
    "directory.description": "RackLoader доверяет активным открытым ключам из этого списка.",
    "directory.refresh": "Обновить список",
    "directory.loading": "Загрузка реестра…",
    "directory.empty": "Издателей пока нет. Создайте ссылку активации выше.",
    "directory.anyMod": "Любой ID мода",
    "directory.added": "Добавлен",
    "directory.active": "АКТИВЕН",
    "directory.revoked": "ОТОЗВАН",
    "directory.revoke": "Отозвать",
    "directory.revokeConfirm": "Отозвать ключ издателя? Новые подписи перестанут выдаваться, а клиенты удалят доверие при следующем обновлении списка ключей.",
    "activation.ready": "ПРИГЛАШЕНИЕ ГОТОВО",
    "activation.title": "Активируйте ключ издателя",
    "activation.description": "Это приглашение оформлено для вас. Активируйте его, чтобы создать ключ для подписи модов. Токен публикации будет показан только один раз.",
    "activation.button": "Активировать ключ",
    "activation.once": "ОДНОРАЗОВАЯ АКТИВАЦИЯ",
    "activation.invalid": "Ссылка активации недействительна, неполная, просрочена или уже использована.",
    "activation.creating": "Создаём ключ издателя…",
    "activation.keyActive": "КЛЮЧ АКТИВЕН",
    "activation.successTitle": "Ключ активирован",
    "activation.successDescription": "Сохраните токен публикации сейчас. После закрытия страницы посмотреть его повторно не получится.",
    "activation.tokenTitle": "Токен разработчика",
    "activation.tokenOnce": "ПОКАЗАН ОДИН РАЗ",
    "activation.publisher": "ИЗДАТЕЛЬ",
    "activation.keyId": "ID КЛЮЧА",
    "activation.modId": "РАЗРЕШЁННЫЙ ID МОДА",
    "activation.anyMod": "Любой ID мода",
    "activation.packerHint": "ДОБАВЬТЕ В CI КАК RACKLOADER_DEVELOPER_KEY",
    "footer.format": "ФОРМАТ RACK MOD <b>01</b>",
    "error.unauthorized": "Токен администратора неверен или срок сессии истёк.",
    "error.invalidInvite": "Ссылка активации недействительна, просрочена или уже использована.",
    "error.developerMissing": "Активный издатель не найден.",
    "error.requestFailed": "Не удалось выполнить запрос. Проверьте введённые данные и попробуйте снова.",
    "error.serviceUnavailable": "Не удалось выполнить запрос. Попробуйте позже.",
    "error.clipboard": "Не удалось скопировать автоматически. Выделите и скопируйте значение вручную."
  }
};

let currentLanguage = localStorage.getItem(languageStorageKey) === "ru" ? "ru" : "en";
let workerOnline = null;
let developerItems = null;

applyLanguage();
document.querySelectorAll("[data-language]").forEach((button) => {
  button.addEventListener("click", () => setLanguage(button.dataset.language));
});

apiHealth();
if (activationCode !== null) {
  showActivationPage();
} else {
  restoreAdminSession();
}

adminLoginForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const token = adminTokenInput.value.trim();
  clearMessage(adminLoginMessage);
  const button = adminLoginForm.querySelector("button[type=submit]");
  button.disabled = true;
  sessionStorage.setItem(adminTokenStorageKey, token);
  try {
    await loadDevelopers();
    showAdminArea(true);
    adminTokenInput.value = "";
  } catch (error) {
    sessionStorage.removeItem(adminTokenStorageKey);
    showError(adminLoginMessage, error);
  } finally {
    button.disabled = false;
  }
});

document.getElementById("admin-logout").addEventListener("click", () => {
  sessionStorage.removeItem(adminTokenStorageKey);
  developerItems = null;
  showAdminArea(false);
  document.getElementById("developer-list").innerHTML = `<div class="empty-state" data-i18n="directory.loading"></div>`;
  applyLanguage();
});

document.getElementById("invite-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  const message = document.getElementById("admin-message");
  const result = document.getElementById("invite-result");
  clearMessage(message);
  result.classList.add("hidden");
  const button = form.querySelector("button[type=submit]");
  button.disabled = true;
  try {
    const data = await adminApi("/api/admin/invites", {
      method: "POST",
      body: Object.fromEntries(new FormData(form).entries())
    });
    const expiry = new Date(data.expiresAt).toLocaleString(currentLanguage === "ru" ? "ru-RU" : "en-US");
    result.innerHTML = `<div class="result-head"><b data-i18n="invite.resultTitle"></b><span data-i18n="invite.oneTime"></span></div>
      <div class="result-row"><span data-i18n="invite.urlLabel"></span><div class="result-value"><code>${escapeHtml(data.activationUrl)}</code><button class="copy-button" data-copy="${escapeHtml(data.activationUrl)}" data-i18n="copy.button"></button></div></div>
      <div class="result-row"><span data-i18n="invite.expiresLabel"></span><div class="result-value"><code data-locale-date="${escapeHtml(data.expiresAt)}"></code></div></div>
      <p class="secret-note" data-i18n="invite.note"></p>`;
    result.classList.remove("hidden");
    result.querySelector("[data-copy]").addEventListener("click", copyButtonValue);
    form.reset();
    applyLanguage();
  } catch (error) {
    showError(message, error);
    if (error.status === 401) showAdminArea(false);
  } finally {
    button.disabled = false;
  }
});

document.getElementById("refresh-developers").addEventListener("click", async () => {
  try { await loadDevelopers(); }
  catch (error) { showError(document.getElementById("admin-message"), error); }
});

document.getElementById("activate-key-button").addEventListener("click", async (event) => {
  const button = event.currentTarget;
  const message = document.getElementById("activation-message");
  button.disabled = true;
  setMessage(message, "activation.creating");
  try {
    const data = await api("/api/enroll", { method: "POST", body: { code: activationCode } });
    window.history.replaceState({}, document.title, window.location.pathname);
    document.getElementById("activation-prompt").classList.add("hidden");
    const result = document.getElementById("activation-result");
    result.innerHTML = `<div class="activation-emblem"><span>R</span><i></i></div>
      <div class="activation-status"><span class="status-dot"></span><span data-i18n="activation.keyActive"></span></div>
      <h2 data-i18n="activation.successTitle"></h2>
      <p data-i18n="activation.successDescription"></p>
      <div class="result-card activation-token-card">
        <div class="result-head"><b data-i18n="activation.tokenTitle"></b><span data-i18n="activation.tokenOnce"></span></div>
        <div class="result-value token-value"><code>${escapeHtml(data.developerToken)}</code><button class="copy-button" data-copy="${escapeHtml(data.developerToken)}" data-i18n="copy.button"></button></div>
      </div>
      <div class="activation-details"><span><small data-i18n="activation.publisher"></small><b>${escapeHtml(data.displayName)}</b></span><span><small data-i18n="activation.keyId"></small><code>${escapeHtml(data.keyId)}</code></span><span><small data-i18n="activation.modId"></small><code>${data.allowedModId ? escapeHtml(data.allowedModId) : `<span data-i18n="activation.anyMod"></span>`}</code></span></div>
      <div class="activation-foot"><span data-i18n="activation.packerHint"></span><span>RACK MOD <b>01</b></span></div>`;
    result.classList.remove("hidden");
    result.querySelector("[data-copy]").addEventListener("click", copyButtonValue);
    clearMessage(message);
    applyLanguage();
  } catch (error) {
    showError(message, error);
    button.disabled = false;
  }
});

async function loadDevelopers() {
  const data = await adminApi("/api/admin/developers");
  renderDevelopers(data.developers);
}

function renderDevelopers(developers) {
  developerItems = developers;
  const container = document.getElementById("developer-list");
  if (!developers.length) {
    container.innerHTML = `<div class="empty-state" data-i18n="directory.empty"></div>`;
    applyLanguage();
    return;
  }
  container.innerHTML = developers.map((developer) => {
    const revoked = Boolean(developer.revokedAt);
    const allowedMod = developer.allowedModId || t("directory.anyMod");
    const addedDate = new Date(developer.createdAt).toLocaleDateString(currentLanguage === "ru" ? "ru-RU" : "en-US");
    return `<article class="developer-row">
      <div class="developer-main"><div class="developer-avatar">${escapeHtml((developer.displayName || "?").slice(0, 1).toUpperCase())}</div>
        <div class="developer-info"><strong>${escapeHtml(developer.displayName)}</strong><code>${escapeHtml(developer.keyId)}</code><small>${escapeHtml(allowedMod)} · <span data-i18n="directory.added"></span> ${escapeHtml(addedDate)}</small></div>
      </div><div class="developer-meta"><span class="badge ${revoked ? "revoked" : ""}" data-i18n="${revoked ? "directory.revoked" : "directory.active"}"></span>${revoked ? "" : `<button class="revoke-button" data-revoke="${escapeHtml(developer.id)}" data-i18n="directory.revoke"></button>`}</div>
    </article>`;
  }).join("");
  container.querySelectorAll("[data-revoke]").forEach((button) => {
    button.addEventListener("click", async () => {
      if (!confirm(t("directory.revokeConfirm"))) return;
      button.disabled = true;
      try {
        await adminApi("/api/admin/revoke", { method: "POST", body: { developerId: button.dataset.revoke } });
        await loadDevelopers();
      } catch (error) {
        showError(document.getElementById("admin-message"), error);
        button.disabled = false;
      }
    });
  });
  applyLanguage();
}

async function adminApi(path, options = {}) {
  const token = sessionStorage.getItem(adminTokenStorageKey);
  if (!token) {
    const error = new Error(t("error.unauthorized"));
    error.translationKey = "error.unauthorized";
    error.status = 401;
    throw error;
  }
  return api(path, options, token);
}

async function api(path, options = {}, bearer) {
  const headers = new Headers(options.headers || {});
  if (options.body !== undefined) headers.set("Content-Type", "application/json");
  if (bearer) headers.set("Authorization", `Bearer ${bearer}`);
  const response = await fetch(path, {
    method: options.method || "GET",
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    cache: "no-store"
  });
  let data;
  try { data = await response.json(); }
  catch { data = {}; }
  if (!response.ok) {
    const message = data.error || `Request failed (${response.status}).`;
    const error = new Error(localizeApiError(message, response.status, path));
    error.status = response.status;
    error.translationKey = apiErrorKey(message, response.status, path);
    throw error;
  }
  return data;
}

function showActivationPage() {
  adminLoginView.classList.add("hidden");
  adminArea.classList.add("hidden");
  activationView.classList.remove("hidden");
  document.body.classList.add("activation-mode");
  document.getElementById("hero-eyebrow").dataset.i18n = "hero.activation.eyebrow";
  document.getElementById("hero-title").dataset.i18nHtml = "hero.activation.title";
  document.getElementById("hero-description").dataset.i18n = "hero.activation.description";
  document.getElementById("hero-points").dataset.i18nHtml = "hero.activation.points";
  if (!/^rki_[A-Za-z0-9_-]{20,100}$/.test(activationCode || "")) {
    document.getElementById("activation-prompt").classList.add("hidden");
    setMessage(document.getElementById("activation-message"), "activation.invalid");
  }
  applyLanguage();
}

async function restoreAdminSession() {
  const restoredAdminToken = sessionStorage.getItem(adminTokenStorageKey);
  if (!restoredAdminToken) return;
  try {
    await loadDevelopers();
    showAdminArea(true);
  } catch {
    sessionStorage.removeItem(adminTokenStorageKey);
  }
}

function showAdminArea(visible) {
  adminArea.classList.toggle("hidden", !visible);
  adminLoginView.classList.toggle("hidden", visible);
}

function setLanguage(language) {
  if (language !== "en" && language !== "ru") return;
  currentLanguage = language;
  localStorage.setItem(languageStorageKey, language);
  applyLanguage();
  if (developerItems !== null && !adminArea.classList.contains("hidden")) renderDevelopers(developerItems);
}

function applyLanguage() {
  document.documentElement.lang = currentLanguage;
  document.title = t("document.title");
  const description = document.querySelector('meta[name="description"]');
  if (description) description.content = t("meta.description");
  document.querySelectorAll("[data-i18n]").forEach((element) => {
    element.textContent = t(element.dataset.i18n);
  });
  document.querySelectorAll("[data-i18n-html]").forEach((element) => {
    element.innerHTML = t(element.dataset.i18nHtml);
  });
  document.querySelectorAll("[data-i18n-placeholder]").forEach((element) => {
    element.placeholder = t(element.dataset.i18nPlaceholder);
  });
  document.querySelectorAll("[data-i18n-aria-label]").forEach((element) => {
    element.setAttribute("aria-label", t(element.dataset.i18nAriaLabel));
  });
  document.querySelectorAll("[data-locale-date]").forEach((element) => {
    const date = new Date(element.dataset.localeDate);
    element.textContent = date.toLocaleString(currentLanguage === "ru" ? "ru-RU" : "en-US");
  });
  document.querySelectorAll("[data-i18n-content]").forEach((element) => {
    element.setAttribute("content", t(element.dataset.i18nContent));
  });
  document.querySelectorAll("[data-language]").forEach((button) => {
    button.setAttribute("aria-pressed", String(button.dataset.language === currentLanguage));
  });
  if (workerOnline !== null) {
    const label = document.querySelector(".network-status .status-label");
    label.textContent = t(workerOnline ? "header.online" : "header.offline");
    label.style.color = workerOnline ? "" : "#e49797";
  }
}

function apiErrorKey(message, status, path) {
  if (message === "Admin authentication is required.") return "error.unauthorized";
  if (message === "This invite code is invalid, expired, or already used.") return "error.invalidInvite";
  if (message === "Active developer was not found.") return "error.developerMissing";
  if (message === "The service could not complete the request.") return "error.serviceUnavailable";
  if (path === "/api/enroll" && status === 400) return "error.invalidInvite";
  if (status === 401 && path.startsWith("/api/admin/")) return "error.unauthorized";
  if (status >= 500) return "error.serviceUnavailable";
  if (currentLanguage === "ru") return "error.requestFailed";
  return null;
}

function localizeApiError(message, status, path) {
  const key = apiErrorKey(message, status, path);
  return key ? t(key) : message;
}

function showError(element, error) {
  clearMessage(element);
  if (error.translationKey) {
    element.dataset.i18n = error.translationKey;
    element.textContent = t(error.translationKey);
  } else {
    element.textContent = error.message;
  }
}

function setMessage(element, key) {
  element.dataset.i18n = key;
  element.textContent = t(key);
}

function clearMessage(element) {
  element.removeAttribute("data-i18n");
  element.textContent = "";
}

function t(key) {
  return translations[currentLanguage][key] || translations.en[key] || key;
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
  })[character]);
}

async function copyButtonValue(event) {
  const button = event.currentTarget;
  try {
    await navigator.clipboard.writeText(button.dataset.copy);
    button.dataset.i18n = "copy.done";
    button.textContent = t("copy.done");
    window.setTimeout(() => {
      if (!button.isConnected) return;
      button.dataset.i18n = "copy.button";
      button.textContent = t("copy.button");
    }, 1300);
  } catch {
    const host = button.closest(".result-card");
    let note = host.querySelector(".copy-fallback");
    if (!note) {
      note = document.createElement("p");
      note.className = "secret-note copy-fallback";
      note.dataset.i18n = "error.clipboard";
      host.append(note);
    }
    note.textContent = t("error.clipboard");
  }
}

function apiHealth() {
  api("/api/health").then(() => setWorkerStatus(true)).catch(() => setWorkerStatus(false));
}

function setWorkerStatus(isOnline) {
  workerOnline = isOnline;
  applyLanguage();
}




