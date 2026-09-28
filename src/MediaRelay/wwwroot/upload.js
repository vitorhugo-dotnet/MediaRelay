(() => {
  const allowed = new Map([
    [".png", "image/png"], [".jpg", "image/jpeg"], [".jpeg", "image/jpeg"],
    [".gif", "image/gif"], [".webp", "image/webp"], [".mp4", "video/mp4"]
  ]);
  const form = document.querySelector("#upload-form");
  const input = document.querySelector("#file-input");
  const dropZone = document.querySelector("#drop-zone");
  const status = document.querySelector("#status");
  const message = document.querySelector("#status-message");
  const progress = document.querySelector("#progress");
  const button = document.querySelector("#upload-button");
  const result = document.querySelector("#result");
  const urlField = document.querySelector("#public-url");
  const maxBytes = Number(document.querySelector(".panel").dataset.maxBytes);
  let droppedFile = null;

  function showMessage(text, isError = false) {
    status.hidden = false;
    message.textContent = text;
    message.classList.toggle("error", isError);
  }

  function selected(file) {
    if (!file) return;
    const dot = file.name.lastIndexOf(".");
    const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : "";
    if (!allowed.has(extension) || file.type.toLowerCase() !== allowed.get(extension)) {
      showMessage("Choose a PNG, JPEG, GIF, WebP or MP4 file.", true);
      input.value = "";
      return;
    }
    if (file.size <= 0 || file.size > maxBytes) {
      showMessage(`This file must be smaller than ${formatBytes(maxBytes)}.`, true);
      input.value = "";
      return;
    }
    document.querySelector("#file-name").textContent = `${file.name} · ${formatBytes(file.size)}`;
    result.hidden = true;
    status.hidden = true;
  }

  function formatBytes(bytes) {
    return new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(bytes / 1024 / 1024) + " MB";
  }

  function apiError(body, fallback) {
    return body?.detail || body?.title || fallback;
  }

  function request(url, body) {
    return fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    }).then(async response => {
      const value = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(apiError(value, "The upload request could not be completed."));
      return value;
    });
  }

  function uploadDirectly(authorization, file) {
    return new Promise((resolve, reject) => {
      const data = new FormData();
      for (const [key, value] of Object.entries(authorization.fields)) data.append(key, value);
      data.append("file", file, file.name);
      const xhr = new XMLHttpRequest();
      xhr.open("POST", authorization.url);
      xhr.upload.onprogress = event => {
        if (event.lengthComputable) progress.value = Math.round((event.loaded / event.total) * 100);
      };
      xhr.onload = () => xhr.status >= 200 && xhr.status < 300
        ? resolve()
        : reject(new Error("Storage rejected the file. Check the file type or try again."));
      xhr.onerror = () => reject(new Error("Could not reach media storage. Check your connection and try again."));
      xhr.onabort = () => reject(new Error("The upload was interrupted. Please try again."));
      xhr.send(data);
    });
  }

  input.addEventListener("change", () => { droppedFile = null; selected(input.files[0]); });
  for (const name of ["dragenter", "dragover"]) dropZone.addEventListener(name, event => {
    event.preventDefault();
    dropZone.classList.add("dragging");
  });
  for (const name of ["dragleave", "drop"]) dropZone.addEventListener(name, event => {
    event.preventDefault();
    dropZone.classList.remove("dragging");
  });
  dropZone.addEventListener("drop", event => {
    const file = event.dataTransfer.files[0];
    if (!file) return;
    try { input.files = event.dataTransfer.files; } catch { /* Selection still works through the dropped File. */ }
    droppedFile = file;
    selected(file);
  });

  form.addEventListener("submit", async event => {
    event.preventDefault();
    const selectedFile = droppedFile || input.files[0];
    if (!selectedFile) {
      showMessage("Choose a file before uploading.", true);
      return;
    }
    button.disabled = true;
    result.hidden = true;
    progress.hidden = false;
    progress.value = 0;
    showMessage("Preparing your upload…");
    try {
      const token = decodeURIComponent(location.pathname.split("/").pop());
      const authorization = await request("/api/uploads/prepare", {
        sessionToken: token,
        fileName: selectedFile.name,
        contentType: selectedFile.type,
        size: selectedFile.size
      });
      showMessage("Uploading directly to media storage…");
      await uploadDirectly(authorization, selectedFile);
      progress.value = 100;
      showMessage("Finishing your upload…");
      const completed = await request("/api/uploads/complete", { sessionToken: token });
      urlField.value = completed.url;
      result.hidden = false;
      showMessage("Upload complete. Your link is ready.");
    } catch (error) {
      showMessage(error instanceof Error ? error.message : "The upload failed. Please try again.", true);
    } finally {
      button.disabled = false;
    }
  });

  document.querySelector("#copy-button").addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(urlField.value);
      showMessage("Link copied.");
    } catch {
      urlField.select();
      showMessage("Select and copy the link above.", true);
    }
  });
})();
