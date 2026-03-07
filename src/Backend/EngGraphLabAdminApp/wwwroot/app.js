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
  passwordDownloads: document.getElementById("passwordDownloads"),
  passwordDownloadsList: document.getElementById("passwordDownloadsList"),
  executionLogs: document.getElementById("executionLogs"),
  executionWarnings: document.getElementById("executionWarnings"),
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

  els.btnPreview.addEventListener("click", () => runProvisioningAction(async () => {
    const files = requireFiles();
    if (!files) return;
    const body = new FormData();
    appendFiles(body, files);
    const result = await request("POST", "/api/provisioning/preview", body, true);
    renderProvisioning(result);
  }));

  els.btnDryRun.addEventListener("click", () => runProvisioningAction(async () => {
    const files = requireFiles();
    if (!files) return;
    const body = new FormData();
    appendFiles(body, files);
    const result = await request("POST", "/api/provisioning/execute?dryRun=true", body, true);
    renderProvisioning(result);
  }));

  els.btnExecute.addEventListener("click", () => runProvisioningAction(async () => {
    const files = requireFiles();
    if (!files) return;
    if (!confirm("Execute provisioning on server?")) return;
    const body = new FormData();
    appendFiles(body, files);
    const result = await request("POST", "/api/provisioning/execute?dryRun=false", body, true);
    renderProvisioning(result);
  }));
}

async function loadConfiguredView() {
  const result = await request("GET", "/api/tflex/config-view");
  const lines = [
    ["Server", result.server],
    ["User", result.userName],
    ["UseAccessToken", String(result.useAccessToken)],
    ["ConfigurationGuid", result.configurationGuid ?? "null"],
    ["Communication", result.communicationMode],
    ["Serializer", result.dataSerializerAlgorithm],
    ["Compression", result.compressionAlgorithm],
    ["AdapterExecutablePath", result.adapterExecutablePath],
    ["AdapterTimeoutSeconds", String(result.adapterTimeoutSeconds)]
  ];
  els.configuredView.innerHTML = lines
    .map(([k, v]) => `<div><strong>${escapeHtml(k)}</strong><span>${escapeHtml(v ?? "")}</span></div>`)
    .join("");

  const form = els.customConnectionForm;
  form.server.value = result.server ?? "";
  form.userName.value = result.userName ?? "";
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
    communicationMode: f.communicationMode.value,
    dataSerializerAlgorithm: f.dataSerializerAlgorithm.value,
    compressionAlgorithm: f.compressionAlgorithm.value
  };
}

function appendFiles(formData, files) {
  files.forEach((file) => formData.append("files", file));
}

function requireFiles() {
  const files = Array.from(els.fileInput.files ?? []);
  if (files.length === 0) {
    renderJson(els.provisioningResult, { error: "Сначала выберите один или несколько CSV/XML/XLSX файлов." });
    return null;
  }
  return files;
}

function renderProvisioning(payload) {
  const groups = extractGroups(payload);
  const students = groups.flatMap((g) => (g.parse?.students ?? []).map((s) => ({ student: s, group: g.parse?.groupName ?? "-" })));
  const actions = groups.flatMap((g) => (g.plan?.actions ?? []).map((a) => ({ action: a, group: g.plan?.groupName ?? g.parse?.groupName ?? "-" })));

  const groupNames = groups
    .map((g) => g.plan?.groupName ?? g.parse?.groupName)
    .filter((x) => Boolean(x));

  els.groupName.textContent = groupNames.length ? groupNames.join(", ") : "-";
  els.studentsCount.textContent = String(payload?.studentsCount ?? students.length);
  els.actionsCount.textContent = String(payload?.actionsCount ?? actions.length);

  els.studentsTableBody.innerHTML = students
    .map((item, i) => `<tr>
      <td>${i + 1}</td>
      <td>${escapeHtml(item.group)}</td>
      <td>${escapeHtml(item.student.login)}</td>
      <td>${escapeHtml(item.student.fullName)}</td>
      <td>${escapeHtml(item.student.pinCode)}</td>
    </tr>`)
    .join("");

  els.actionsTableBody.innerHTML = actions
    .map((item) => `<tr>
      <td>${escapeHtml(item.group)}</td>
      <td>${escapeHtml(item.action.step)}</td>
      <td>${escapeHtml(item.action.target)}</td>
      <td>${escapeHtml(item.action.details)}</td>
    </tr>`)
    .join("");

  renderPasswordDownloads(groups);
  renderExecutionDetails(payload, groups);
  renderJson(els.provisioningResult, payload);
}

function extractGroups(payload) {
  if (Array.isArray(payload?.groups) && payload.groups.length > 0) {
    return payload.groups;
  }

  if (payload?.parse || payload?.plan || payload?.passwordExport || payload?.execution) {
    return [{
      fileName: payload?.parse?.groupName ?? "group",
      parse: payload?.parse ?? null,
      plan: payload?.plan ?? null,
      passwordExport: payload?.passwordExport ?? null,
      execution: payload?.execution ?? null
    }];
  }

  return [];
}

function renderPasswordDownloads(groups) {
  const entries = groups
    .map((g) => {
      const exp = g.passwordExport;
      if (!exp?.csvUrl || !exp?.xlsxUrl) return null;
      const groupName = g.parse?.groupName ?? exp.groupName ?? "group";
      return { groupName, csvUrl: exp.csvUrl, xlsxUrl: exp.xlsxUrl };
    })
    .filter((x) => x);

  if (entries.length === 0) {
    els.passwordDownloads.hidden = true;
    els.passwordDownloadsList.innerHTML = "";
    return;
  }

  els.passwordDownloadsList.innerHTML = entries
    .map((entry) => `<div>
      <strong>${escapeHtml(entry.groupName)}</strong>
      <a class="download-link" href="${escapeHtml(entry.csvUrl)}" target="_blank" rel="noopener">CSV</a>
      <a class="download-link" href="${escapeHtml(entry.xlsxUrl)}" target="_blank" rel="noopener">XLSX</a>
    </div>`)
    .join("");
  els.passwordDownloads.hidden = false;
}

function renderExecutionDetails(payload, groups) {
  const logs = [];
  const warnings = [];
  const isInfoDiagnostic = (text) => String(text ?? "").toLowerCase().startsWith("connection attempt:");

  groups.forEach((group) => {
    const groupName = group?.parse?.groupName ?? group?.plan?.groupName ?? "group";

    (group?.parse?.errors ?? []).forEach((item) => warnings.push(`[${groupName}] ${item}`));
    (group?.parse?.warnings ?? []).forEach((item) => warnings.push(`[${groupName}] ${item}`));
    (group?.execution?.warnings ?? []).forEach((item) => warnings.push(`[${groupName}] ${item}`));
    (group?.execution?.diagnostics ?? []).forEach((item) => {
      if (!isInfoDiagnostic(item)) {
        warnings.push(`[${groupName}] ${item}`);
      }
    });
    (group?.execution?.logs ?? []).forEach((item) => logs.push(`[${groupName}] ${item}`));
  });

  (payload?.connection?.missingDependencies ?? []).forEach((item) => {
    if (!isInfoDiagnostic(item)) {
      warnings.push(`[connection] ${item}`);
    }
  });
  if (payload?.message) {
    warnings.push(payload.message);
  }

  els.executionLogs.innerHTML = logs.length
    ? logs.map((item) => `<li>${escapeHtml(item)}</li>`).join("")
    : `<li>${escapeHtml("Логов пока нет.")}</li>`;

  els.executionWarnings.innerHTML = warnings.length
    ? warnings.map((item) => `<li>${escapeHtml(item)}</li>`).join("")
    : `<li>${escapeHtml("Предупреждений нет.")}</li>`;
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

async function runProvisioningAction(action) {
  try {
    await action();
  } catch (error) {
    renderProvisioning(error);
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
