const els = {
  configuredView: document.getElementById("configuredView"),
  connectionResult: document.getElementById("connectionResult"),
  provisioningResult: document.getElementById("provisioningResult"),
  customConnectionForm: document.getElementById("customConnectionForm"),
  fileInput: document.getElementById("fileInput"),
  groupName: document.getElementById("groupName"),
  studentsCount: document.getElementById("studentsCount"),
  actionsCount: document.getElementById("actionsCount"),
  studentsTableBody: document.querySelector("#studentsTable tbody"),
  actionsTableBody: document.querySelector("#actionsTable tbody"),
  btnCheckConfigured: document.getElementById("btnCheckConfigured"),
  btnCheckCustom: document.getElementById("btnCheckCustom"),
  btnPreview: document.getElementById("btnPreview"),
  btnDryRun: document.getElementById("btnDryRun"),
  btnExecute: document.getElementById("btnExecute")
};

boot().catch((error) => {
  els.connectionResult.textContent = "Init error: " + String(error);
});

async function boot() {
  await loadConfiguredView();
  bindEvents();
}

function bindEvents() {
  els.btnCheckConfigured.addEventListener("click", () => runAction(async () => {
    const result = await request("POST", "/api/tflex/check-connection");
    renderJson(els.connectionResult, result);
  }, els.connectionResult));

  els.btnCheckCustom.addEventListener("click", () => runAction(async () => {
    const requestBody = readConnectionForm();
    const result = await request("POST", "/api/tflex/check-connection/custom", requestBody);
    renderJson(els.connectionResult, result);
  }, els.connectionResult));

  els.btnPreview.addEventListener("click", () => runAction(async () => {
    const file = requireFile();
    if (!file) return;
    const body = new FormData();
    body.append("file", file);
    const result = await request("POST", "/api/provisioning/preview", body, true);
    renderProvisioning(result);
  }, els.provisioningResult));

  els.btnDryRun.addEventListener("click", () => runAction(async () => {
    const file = requireFile();
    if (!file) return;
    const body = new FormData();
    body.append("file", file);
    const result = await request("POST", "/api/provisioning/execute?dryRun=true", body, true);
    renderProvisioning(result);
  }, els.provisioningResult));

  els.btnExecute.addEventListener("click", () => runAction(async () => {
    const file = requireFile();
    if (!file) return;
    if (!confirm("Execute provisioning on server?")) return;
    const body = new FormData();
    body.append("file", file);
    const result = await request("POST", "/api/provisioning/execute?dryRun=false", body, true);
    renderProvisioning(result);
  }, els.provisioningResult));
}

async function loadConfiguredView() {
  const result = await request("GET", "/api/tflex/config-view");
  const lines = [
    ["Server", result.server],
    ["User", result.userName],
    ["UseAccessToken", String(result.useAccessToken)],
    ["ConfigurationGuid", result.configurationGuid ?? "null"],
    ["ClientProgramDirectory", result.clientProgramDirectory],
    ["Communication", result.communicationMode],
    ["Serializer", result.dataSerializerAlgorithm],
    ["Compression", result.compressionAlgorithm]
  ];
  els.configuredView.innerHTML = lines
    .map(([k, v]) => `<div><strong>${escapeHtml(k)}</strong><span>${escapeHtml(v ?? "")}</span></div>`)
    .join("");

  const form = els.customConnectionForm;
  form.server.value = result.server ?? "";
  form.userName.value = result.userName ?? "";
  form.clientProgramDirectory.value = result.clientProgramDirectory ?? "";
  form.communicationMode.value = result.communicationMode ?? "GRPC";
  form.dataSerializerAlgorithm.value = result.dataSerializerAlgorithm ?? "Default";
  form.compressionAlgorithm.value = result.compressionAlgorithm ?? "None";
}

function readConnectionForm() {
  const f = els.customConnectionForm;
  const guidRaw = f.configurationGuid.value.trim();
  return {
    server: f.server.value.trim(),
    userName: f.userName.value.trim(),
    password: f.password.value,
    useAccessToken: f.useAccessToken.value === "true",
    accessToken: f.accessToken.value.trim(),
    configurationGuid: guidRaw ? guidRaw : null,
    clientProgramDirectory: f.clientProgramDirectory.value.trim(),
    communicationMode: f.communicationMode.value,
    dataSerializerAlgorithm: f.dataSerializerAlgorithm.value,
    compressionAlgorithm: f.compressionAlgorithm.value
  };
}

function requireFile() {
  const file = els.fileInput.files?.[0];
  if (!file) {
    renderJson(els.provisioningResult, { error: "Select CSV or XML file first." });
    return null;
  }
  return file;
}

function renderProvisioning(payload) {
  const parse = payload?.parse ?? null;
  const plan = payload?.plan ?? payload?.execution?.plan ?? payload?.connection?.plan ?? payload?.plan;
  const students = parse?.students ?? [];
  const actions = plan?.actions ?? [];

  els.groupName.textContent = plan?.groupName ?? parse?.groupName ?? "-";
  els.studentsCount.textContent = String(students.length);
  els.actionsCount.textContent = String(actions.length);

  els.studentsTableBody.innerHTML = students
    .map((s, i) => `<tr>
      <td>${i + 1}</td>
      <td>${escapeHtml(s.login)}</td>
      <td>${escapeHtml(s.fullName)}</td>
      <td>${escapeHtml(s.pinCode)}</td>
    </tr>`)
    .join("");

  els.actionsTableBody.innerHTML = actions
    .map((a) => `<tr>
      <td>${escapeHtml(a.step)}</td>
      <td>${escapeHtml(a.target)}</td>
      <td>${escapeHtml(a.details)}</td>
    </tr>`)
    .join("");

  renderJson(els.provisioningResult, payload);
}

async function request(method, url, body, isFormData = false) {
  const init = { method, headers: {} };
  if (body !== undefined) {
    if (isFormData) {
      init.body = body;
    } else {
      init.headers["Content-Type"] = "application/json";
      init.body = JSON.stringify(body);
    }
  }

  const res = await fetch(url, init);
  const text = await res.text();
  let json;
  try {
    json = text ? JSON.parse(text) : {};
  } catch {
    json = { raw: text };
  }
  if (!res.ok) throw json;
  return json;
}

function renderJson(target, value) {
  target.textContent = JSON.stringify(value, null, 2);
}

async function runAction(action, output) {
  try {
    await action();
  } catch (error) {
    renderJson(output, error);
  }
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}
