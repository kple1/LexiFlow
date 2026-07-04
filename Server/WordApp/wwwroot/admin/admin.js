let token = sessionStorage.getItem("adminToken") || "";

function ensureToken() {
  if (!token) {
    token = prompt("관리자 토큰을 입력하세요") || "";
    sessionStorage.setItem("adminToken", token);
  }
  return token;
}

// Returns { ok: true, data } on success, { ok: false } once the failure has
// already been reported to the user (alert), so callers just check `.ok`.
async function api(path, options = {}) {
  ensureToken();
  const res = await fetch(path, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      "X-Admin-Token": token,
      ...(options.headers || {}),
    },
  });

  if (res.status === 401) {
    alert("토큰이 올바르지 않습니다. 다시 입력해주세요.");
    sessionStorage.removeItem("adminToken");
    token = "";
    ensureToken();
    return api(path, options);
  }

  if (res.status === 409) {
    alert(await res.text());
    return { ok: false };
  }

  if (!res.ok) {
    alert(`요청 실패 (${res.status})`);
    return { ok: false };
  }

  return { ok: true, data: res.status === 204 ? null : await res.json() };
}

// ---- Tabs ----
document.querySelectorAll(".tab-btn").forEach((btn) => {
  btn.addEventListener("click", () => {
    document.querySelectorAll(".tab-btn").forEach((b) => b.classList.remove("active"));
    document.querySelectorAll(".tab-panel").forEach((p) => p.classList.remove("active"));
    btn.classList.add("active");
    document.getElementById(`tab-${btn.dataset.tab}`).classList.add("active");
  });
});

// ---- Words ----
const wordForm = document.getElementById("word-form");
const wordCancelBtn = document.getElementById("word-cancel");

async function loadWords() {
  const res = await api("/admin/api/words");
  if (!res.ok) return;
  const tbody = document.querySelector("#word-table tbody");
  tbody.innerHTML = "";
  for (const w of res.data) {
    const readonly = w.source !== "Manual";
    const tr = document.createElement("tr");
    if (readonly) tr.classList.add("readonly");
    tr.innerHTML = `
      <td>${escapeHtml(w.english)}</td>
      <td>${escapeHtml(w.meaning)}</td>
      <td>${escapeHtml(w.status)}</td>
      <td>${escapeHtml(w.example ?? "")}</td>
      <td>${w.source}</td>
      <td class="row-actions">
        <button data-act="edit" ${readonly ? "disabled" : ""}>수정</button>
        <button data-act="delete" class="danger" ${readonly ? "disabled" : ""}>삭제</button>
      </td>`;
    tr.querySelector('[data-act="edit"]').addEventListener("click", () => startEditWord(w));
    tr.querySelector('[data-act="delete"]').addEventListener("click", () => deleteWord(w.id));
    tbody.appendChild(tr);
  }
}

function startEditWord(w) {
  wordForm.id.value = w.id;
  wordForm.english.value = w.english;
  wordForm.meaning.value = w.meaning;
  wordForm.status.value = w.status;
  wordForm.example.value = w.example ?? "";
  wordForm.note.value = w.note ?? "";
  wordForm.querySelector("button[type=submit]").textContent = "수정";
  wordCancelBtn.hidden = false;
}

function resetWordForm() {
  wordForm.reset();
  wordForm.id.value = "";
  wordForm.querySelector("button[type=submit]").textContent = "추가";
  wordCancelBtn.hidden = true;
}

wordCancelBtn.addEventListener("click", resetWordForm);

wordForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  const id = wordForm.id.value;
  const dto = {
    english: wordForm.english.value,
    meaning: wordForm.meaning.value,
    status: wordForm.status.value,
    example: wordForm.example.value || null,
    note: wordForm.note.value || null,
  };
  const result = id
    ? await api(`/admin/api/words/${id}`, { method: "PUT", body: JSON.stringify(dto) })
    : await api("/admin/api/words", { method: "POST", body: JSON.stringify(dto) });
  if (result.ok) {
    resetWordForm();
    loadWords();
  }
});

async function deleteWord(id) {
  if (!confirm("이 단어를 삭제할까요?")) return;
  const result = await api(`/admin/api/words/${id}`, { method: "DELETE" });
  if (result.ok) loadWords();
}

// ---- Grammars ----
const grammarForm = document.getElementById("grammar-form");
const grammarCancelBtn = document.getElementById("grammar-cancel");

async function loadGrammars() {
  const res = await api("/admin/api/grammars");
  if (!res.ok) return;
  const tbody = document.querySelector("#grammar-table tbody");
  tbody.innerHTML = "";
  for (const g of res.data) {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td>${escapeHtml(g.title)}</td>
      <td>${escapeHtml(g.category ?? "")}</td>
      <td>${escapeHtml(g.status ?? "")}</td>
      <td>${escapeHtml(g.explanation ?? "")}</td>
      <td class="row-actions">
        <button data-act="edit">수정</button>
        <button data-act="delete" class="danger">삭제</button>
      </td>`;
    tr.querySelector('[data-act="edit"]').addEventListener("click", () => startEditGrammar(g));
    tr.querySelector('[data-act="delete"]').addEventListener("click", () => deleteGrammar(g.id));
    tbody.appendChild(tr);
  }
}

function startEditGrammar(g) {
  grammarForm.id.value = g.id;
  grammarForm.title.value = g.title;
  grammarForm.category.value = g.category ?? "";
  grammarForm.status.value = g.status ?? "미분류";
  grammarForm.example.value = g.example ?? "";
  grammarForm.explanation.value = g.explanation ?? "";
  grammarForm.note.value = g.note ?? "";
  grammarForm.querySelector("button[type=submit]").textContent = "수정";
  grammarCancelBtn.hidden = false;
}

function resetGrammarForm() {
  grammarForm.reset();
  grammarForm.id.value = "";
  grammarForm.querySelector("button[type=submit]").textContent = "추가";
  grammarCancelBtn.hidden = true;
}

grammarCancelBtn.addEventListener("click", resetGrammarForm);

grammarForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  const id = grammarForm.id.value;
  const dto = {
    title: grammarForm.title.value,
    category: grammarForm.category.value,
    example: grammarForm.example.value,
    explanation: grammarForm.explanation.value,
    note: grammarForm.note.value,
    status: grammarForm.status.value,
  };
  const result = id
    ? await api(`/admin/api/grammars/${id}`, { method: "PUT", body: JSON.stringify(dto) })
    : await api("/admin/api/grammars", { method: "POST", body: JSON.stringify(dto) });
  if (result.ok) {
    resetGrammarForm();
    loadGrammars();
  }
});

async function deleteGrammar(id) {
  if (!confirm("이 문법 항목을 삭제할까요?")) return;
  const result = await api(`/admin/api/grammars/${id}`, { method: "DELETE" });
  if (result.ok) loadGrammars();
}

// ---- Idioms ----
const idiomForm = document.getElementById("idiom-form");
const idiomCancelBtn = document.getElementById("idiom-cancel");

async function loadIdioms() {
  const res = await api("/admin/api/idioms");
  if (!res.ok) return;
  const tbody = document.querySelector("#idiom-table tbody");
  tbody.innerHTML = "";
  for (const i of res.data) {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td>${escapeHtml(i.title)}</td>
      <td>${escapeHtml(i.category ?? "")}</td>
      <td>${escapeHtml(i.status ?? "")}</td>
      <td>${escapeHtml(i.explanation ?? "")}</td>
      <td class="row-actions">
        <button data-act="edit">수정</button>
        <button data-act="delete" class="danger">삭제</button>
      </td>`;
    tr.querySelector('[data-act="edit"]').addEventListener("click", () => startEditIdiom(i));
    tr.querySelector('[data-act="delete"]').addEventListener("click", () => deleteIdiom(i.id));
    tbody.appendChild(tr);
  }
}

function startEditIdiom(i) {
  idiomForm.id.value = i.id;
  idiomForm.title.value = i.title;
  idiomForm.category.value = i.category ?? "";
  idiomForm.status.value = i.status ?? "미분류";
  idiomForm.example.value = i.example ?? "";
  idiomForm.explanation.value = i.explanation ?? "";
  idiomForm.note.value = i.note ?? "";
  idiomForm.querySelector("button[type=submit]").textContent = "수정";
  idiomCancelBtn.hidden = false;
}

function resetIdiomForm() {
  idiomForm.reset();
  idiomForm.id.value = "";
  idiomForm.querySelector("button[type=submit]").textContent = "추가";
  idiomCancelBtn.hidden = true;
}

idiomCancelBtn.addEventListener("click", resetIdiomForm);

idiomForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  const id = idiomForm.id.value;
  const dto = {
    title: idiomForm.title.value,
    category: idiomForm.category.value,
    example: idiomForm.example.value,
    explanation: idiomForm.explanation.value,
    note: idiomForm.note.value,
    status: idiomForm.status.value,
  };
  const result = id
    ? await api(`/admin/api/idioms/${id}`, { method: "PUT", body: JSON.stringify(dto) })
    : await api("/admin/api/idioms", { method: "POST", body: JSON.stringify(dto) });
  if (result.ok) {
    resetIdiomForm();
    loadIdioms();
  }
});

async function deleteIdiom(id) {
  if (!confirm("이 숙어/구동사를 삭제할까요?")) return;
  const result = await api(`/admin/api/idioms/${id}`, { method: "DELETE" });
  if (result.ok) loadIdioms();
}

function escapeHtml(str) {
  const div = document.createElement("div");
  div.textContent = str;
  return div.innerHTML;
}

loadWords();
loadGrammars();
loadIdioms();
