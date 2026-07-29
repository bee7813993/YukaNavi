/*
 * ゆかナビ スキンメーカー
 * ブラウザ内で完結するきせかえスキン (skin.json + 素材) の作成ツール。
 * ファイルはどこにも送信されず、zip の生成・読み込みも JSZip でローカル処理する。
 * skin.json の仕様は docs/skin-dlc.md / アプリの SkinManager.cs と対応。
 */
"use strict";

/* ================= 定数 ================= */

const IMAGE_EXT = ["png", "jpg", "jpeg", "webp", "gif"];
const VIDEO_EXT = ["mp4", "webm", "mov"];
const AUDIO_EXT = ["mp3", "ogg", "wav"];
const AUDIO_NG_EXT = ["m4a", "aac"]; // Unity のランタイム読み込み非対応

// 昼夜・季節スロット (背景と BGM で共用)。key は skin.json のサフィックスと一致
const TIMED_KEYS = [
  { key: "day", label: "昼 (6〜18時)" },
  { key: "night", label: "夜 (18〜翌6時)" },
  { key: "spring_day", label: "春・昼 (3〜5月)" },
  { key: "spring_night", label: "春・夜" },
  { key: "summer_day", label: "夏・昼 (6〜8月)" },
  { key: "summer_night", label: "夏・夜" },
  { key: "autumn_day", label: "秋・昼 (9〜11月)" },
  { key: "autumn_night", label: "秋・夜" },
  { key: "winter_day", label: "冬・昼 (12〜2月)" },
  { key: "winter_night", label: "冬・夜" },
];

const SE_KEYS = [
  { key: "tap", label: "タップ音" },
  { key: "confirm", label: "決定音" },
  { key: "error", label: "エラー音" },
  { key: "transition", label: "画面切替音" },
  { key: "complete", label: "予約完了音" },
];

// アプリのテーマ色プリセット (SkinScreen.ThemePresets と同じ)
const THEME_PRESETS = [null, "#E06BA8", "#D65C5C", "#E08A3C", "#4CAF6E", "#3CAAB4", "#4C7FD6", "#6B7280"];

// デフォルト (ゆかりちゃん) のセリフ。プレビュー用 (MascotView.cs と同じ)
const DEFAULT_LINES = ["うたっていこ〜♪", "今日はなにうたう？", "タップありがと♪", "いっぱい予約してね！", "じゅんびばっちりだよ〜"];

/* ================= 状態 ================= */

// asset = { blob, srcName, url, kind: 'image'|'video'|'audio' }
const state = {
  name: "",
  author: "",
  description: "",
  themePrimary: null,
  talk: "",
  talkMorning: "",
  talkEvening: "",
  talkNight: "",
  backgrounds: [], // [{ asset, rotation, zoom, offsetX, offsetY }]
  timedBg: {},     // key -> asset
  characterNone: false,
  characters: [],  // [{ asset, scale, talk, eyesClosed, expressions: [{ asset, talk }] }]
  poseComplete: null,
  bgm: null,
  timedBgm: {},    // key -> asset
  se: {},          // key -> asset
  record: null,
  splash: null,
  thumbnail: null,
  extraJson: {},   // zip 読み込み時の未知キー (再出力で保持)
  extraFiles: [],  // zip 読み込み時の未参照ファイル [{ name, blob }] (再出力で保持)
};

// プレビューの巡回状態
const pv = { charIndex: 0, exprIndex: 0, blinkTimer: null, bubbleTimer: null };

/* ================= ユーティリティ ================= */

function extOf(name) {
  const i = name.lastIndexOf(".");
  return i < 0 ? "" : name.slice(i + 1).toLowerCase();
}

function kindOf(name) {
  const e = extOf(name);
  if (IMAGE_EXT.includes(e)) return "image";
  if (VIDEO_EXT.includes(e)) return "video";
  if (AUDIO_EXT.includes(e)) return "audio";
  return null;
}

function makeAsset(blob, srcName) {
  const kind = kindOf(srcName);
  return { blob, srcName, url: URL.createObjectURL(blob), kind };
}

function freeAsset(asset) {
  if (asset && asset.url) URL.revokeObjectURL(asset.url);
}

function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text !== undefined) e.textContent = text;
  return e;
}

function linesOf(text) {
  const lines = (text || "").split("\n").map((s) => s.trim()).filter((s) => s.length > 0);
  return lines.length > 0 ? lines : null;
}

/* 時刻・季節ヘルパー (SkinManager.cs と同じ区分) */
function seasonOf(month) {
  if (month >= 3 && month <= 5) return "spring";
  if (month >= 6 && month <= 8) return "summer";
  if (month >= 9 && month <= 11) return "autumn";
  return "winter";
}
function isDaytime(hour) { return hour >= 6 && hour < 18; }
function timeOfDaySuffix(hour) {
  if (hour >= 5 && hour < 11) return "morning";
  if (hour >= 17 && hour < 22) return "evening";
  if (hour >= 22 || hour < 5) return "night";
  return null;
}

/* フォールバック解決 (GetBgmFor / GetTimedBackground 相当) */
function resolveTimedKey(month, hour) {
  return seasonOf(month) + "_" + (isDaytime(hour) ? "day" : "night");
}
function resolveBgm(month, hour) {
  const seasonal = state.timedBgm[resolveTimedKey(month, hour)];
  if (seasonal) return { asset: seasonal, label: "季節×昼夜" };
  const timed = state.timedBgm[isDaytime(hour) ? "day" : "night"];
  if (timed) return { asset: timed, label: "昼夜" };
  if (state.bgm) return { asset: state.bgm, label: "通常" };
  return { asset: null, label: "アプリ標準" };
}
function resolveTimedBackground(month, hour) {
  const seasonal = state.timedBg[resolveTimedKey(month, hour)];
  if (seasonal) return { asset: seasonal, label: "季節×昼夜" };
  const timed = state.timedBg[isDaytime(hour) ? "day" : "night"];
  if (timed) return { asset: timed, label: "昼夜" };
  return null;
}

/* ================= ファイル受け入れ ================= */

function acceptFile(file, kinds, slotLabel) {
  const e = extOf(file.name);
  if (AUDIO_NG_EXT.includes(e)) {
    alert(`${slotLabel}: m4a / aac はアプリで再生できません。mp3 / ogg / wav に変換してください`);
    return false;
  }
  const kind = kindOf(file.name);
  if (!kind || !kinds.includes(kind)) {
    const names = kinds.map((k) => ({ image: "画像 (png/jpg/webp/gif)", video: "動画 (mp4/webm/mov)", audio: "音声 (mp3/ogg/wav)" }[k])).join(" / ");
    alert(`${slotLabel}: このファイルは使えません。${names} を選んでください`);
    return false;
  }
  return true;
}

/*
 * ファイルスロット UI を作る。
 * opts: { title, kinds, get(), set(asset|null), pngOnly }
 */
function createSlot(opts) {
  const slot = el("div", "slot empty");
  const thumb = el("div", "slot-thumb");
  const label = el("div", "slot-label");
  const title = el("div", "slot-title", opts.title);
  const fileName = el("div", "slot-file");
  const clear = el("button", "slot-clear", "✕");
  clear.type = "button";
  clear.title = "外す";
  label.append(title, fileName);
  slot.append(thumb, label, clear);

  const input = document.createElement("input");
  input.type = "file";
  input.hidden = true;
  const accepts = [];
  if (opts.kinds.includes("image")) accepts.push(opts.pngOnly ? ".png" : IMAGE_EXT.map((e) => "." + e).join(","));
  if (opts.kinds.includes("video")) accepts.push(VIDEO_EXT.map((e) => "." + e).join(","));
  if (opts.kinds.includes("audio")) accepts.push(AUDIO_EXT.map((e) => "." + e).join(","));
  input.accept = accepts.join(",");
  slot.append(input);

  function render() {
    const asset = opts.get();
    slot.classList.toggle("empty", !asset);
    slot.classList.toggle("filled", !!asset);
    thumb.className = "slot-thumb";
    thumb.style.backgroundImage = "";
    thumb.textContent = "";
    if (!asset) {
      fileName.textContent = "クリック / ドロップでファイルを選ぶ";
    } else {
      fileName.textContent = asset.srcName;
      if (asset.kind === "image") {
        thumb.style.backgroundImage = `url("${asset.url}")`;
        thumb.style.backgroundSize = "cover";
        thumb.style.backgroundPosition = "center";
      } else if (asset.kind === "video") {
        thumb.classList.add("audio");
        thumb.textContent = "🎬";
      } else {
        thumb.classList.add("audio");
        thumb.textContent = "♪";
      }
    }
  }

  function take(file) {
    if (opts.pngOnly && extOf(file.name) !== "png") {
      alert(`${opts.title}: png のみ使えます`);
      return;
    }
    if (!acceptFile(file, opts.kinds, opts.title)) return;
    freeAsset(opts.get());
    opts.set(makeAsset(file, file.name));
    render();
    onStateChanged();
  }

  slot.addEventListener("click", (ev) => {
    if (ev.target === clear) return;
    input.click();
  });
  input.addEventListener("change", () => {
    if (input.files.length > 0) take(input.files[0]);
    input.value = "";
  });
  clear.addEventListener("click", () => {
    freeAsset(opts.get());
    opts.set(null);
    render();
    onStateChanged();
  });
  slot.addEventListener("dragover", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    slot.classList.add("dragover");
  });
  slot.addEventListener("dragleave", () => slot.classList.remove("dragover"));
  slot.addEventListener("drop", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    slot.classList.remove("dragover");
    if (ev.dataTransfer.files.length > 0) take(ev.dataTransfer.files[0]);
  });

  slot.refresh = render;
  render();
  return slot;
}

/* ================= フォーム生成 ================= */

const formRoot = document.getElementById("form-root");
const slotRegistry = []; // 再描画用

function registerSlot(slot) {
  slotRegistry.push(slot);
  return slot;
}

function createSection(titleText, note, open) {
  const details = el("details", "section");
  if (open) details.open = true;
  const summary = el("summary");
  summary.append(document.createTextNode(titleText));
  if (note) summary.append(el("span", "summary-note", note));
  const body = el("div", "section-body");
  details.append(summary, body);
  formRoot.append(details);
  return body;
}

function createTextField(parent, labelText, noteText, getter, setter, multiline) {
  const field = el("div", "field");
  field.append(el("label", null, labelText));
  if (noteText) field.append(el("div", "field-note", noteText));
  const input = document.createElement(multiline ? "textarea" : "input");
  if (!multiline) input.type = "text";
  input.value = getter();
  input.addEventListener("input", () => {
    setter(input.value);
    onStateChanged();
  });
  field.append(input);
  parent.append(field);
  return input;
}

function buildBasicSection() {
  const body = createSection("基本情報", "名前・作者・テーマ色など", true);
  createTextField(body, "スキンの名前", null, () => state.name, (v) => (state.name = v));
  createTextField(body, "作者名 (任意)", "きせかえ一覧に表示されます", () => state.author, (v) => (state.author = v));
  createTextField(body, "説明 (任意)", null, () => state.description, (v) => (state.description = v));

  // テーマ色
  const field = el("div", "field");
  field.append(el("label", null, "テーマ色 (ボタンや文字の色)"));
  const row = el("div", "theme-row");
  const chips = [];
  THEME_PRESETS.forEach((hex) => {
    const chip = el("button", "theme-chip" + (hex ? "" : " none"));
    chip.type = "button";
    if (hex) chip.style.background = hex;
    chip.title = hex || "既定 (紫)";
    chip.addEventListener("click", () => {
      state.themePrimary = hex;
      chips.forEach((c) => c.classList.remove("selected"));
      chip.classList.add("selected");
      customInput.value = hex || "";
      onStateChanged();
    });
    chips.push(chip);
    row.append(chip);
  });
  const customInput = document.createElement("input");
  customInput.type = "text";
  customInput.placeholder = "#RRGGBB";
  customInput.style.width = "110px";
  customInput.addEventListener("input", () => {
    const v = customInput.value.trim();
    if (/^#[0-9a-fA-F]{6}$/.test(v)) {
      state.themePrimary = v;
      chips.forEach((c) => c.classList.remove("selected"));
      onStateChanged();
    } else if (v === "") {
      state.themePrimary = null;
      onStateChanged();
    }
  });
  row.append(customInput);
  field.append(row);
  body.append(field);
  chips[0].classList.add("selected");
  buildBasicSection.syncTheme = () => {
    chips.forEach((c, i) => c.classList.toggle("selected", THEME_PRESETS[i] === state.themePrimary));
    customInput.value = state.themePrimary || "";
  };

  const grid = el("div", "slot-grid");
  grid.append(
    registerSlot(createSlot({ title: "サムネイル (thumbnail.png)", kinds: ["image"], pngOnly: true, get: () => state.thumbnail, set: (a) => (state.thumbnail = a) })),
    registerSlot(createSlot({ title: "起動画面 (splash.png)", kinds: ["image"], pngOnly: true, get: () => state.splash, set: (a) => (state.splash = a) })),
    registerSlot(createSlot({ title: "レコード盤 (リモコン画面)", kinds: ["image"], get: () => state.record, set: (a) => (state.record = a) }))
  );
  body.append(el("div", "field-note", "サムネイルは きせかえ一覧、起動画面はアプリ起動時に使われます (どちらも任意)"));
  body.append(grid);
}

function buildBackgroundSection() {
  const body = createSection("背景", "通常の背景と、昼夜・季節の自動切替", true);
  const list = el("div");
  body.append(el("div", "field-note", "通常の背景 (複数可)。2枚以上あるとホームの背景タップで切り替えられます"));
  body.append(list);
  const addBtn = el("button", "btn small add", "+ 背景を追加");
  addBtn.type = "button";
  addBtn.addEventListener("click", () => {
    state.backgrounds.push({ asset: null, rotation: 0, zoom: 1, offsetX: 0, offsetY: 0 });
    renderBackgrounds();
    onStateChanged();
  });
  body.append(addBtn);

  function renderBackgrounds() {
    list.textContent = "";
    state.backgrounds.forEach((bg, i) => {
      const card = el("div", "item-card");
      const header = el("div", "item-card-header");
      header.append(el("div", "item-title", `背景 ${i + 1}`));
      const del = el("button", "btn small danger", "削除");
      del.type = "button";
      del.addEventListener("click", () => {
        freeAsset(bg.asset);
        state.backgrounds.splice(i, 1);
        renderBackgrounds();
        onStateChanged();
      });
      header.append(del);
      card.append(header);
      card.append(createSlot({ title: "画像 / 動画", kinds: ["image", "video"], get: () => bg.asset, set: (a) => (bg.asset = a) }));

      // 表示調整 (アプリの BackgroundView 相当。プレビューは近似)
      const adj = el("div", "inline-fields");
      adj.style.marginTop = "8px";
      const rotLabel = el("label", null, "回転 ");
      const rotSel = document.createElement("select");
      [0, 90, 180, 270].forEach((d) => {
        const o = el("option", null, d + "°");
        o.value = d;
        rotSel.append(o);
      });
      rotSel.value = bg.rotation;
      rotSel.addEventListener("change", () => { bg.rotation = Number(rotSel.value); onStateChanged(); });
      rotLabel.append(rotSel);
      adj.append(rotLabel);
      adj.append(createNumber("ズーム", bg.zoom, 1, 4, 0.1, (v) => (bg.zoom = v)));
      adj.append(createNumber("横位置", bg.offsetX, -1, 1, 0.05, (v) => (bg.offsetX = v)));
      adj.append(createNumber("縦位置", bg.offsetY, -1, 1, 0.05, (v) => (bg.offsetY = v)));
      card.append(adj);
      list.append(card);
    });
  }
  buildBackgroundSection.render = renderBackgrounds;

  body.append(el("hr"));
  body.append(el("div", "field-note", "昼夜・季節の背景 (任意)。指定した時間帯はこちらが優先され、タップ巡回の1枚目になります。優先順: 季節×昼夜 → 昼夜 → 通常"));
  const grid = el("div", "slot-grid");
  TIMED_KEYS.forEach(({ key, label }) => {
    grid.append(registerSlot(createSlot({
      title: label, kinds: ["image", "video"],
      get: () => state.timedBg[key] || null,
      set: (a) => { if (a) state.timedBg[key] = a; else delete state.timedBg[key]; },
    })));
  });
  body.append(grid);

  function createNumber(labelText, value, min, max, step, setter) {
    const label = el("label", null, labelText + " ");
    const input = document.createElement("input");
    input.type = "number";
    input.min = min; input.max = max; input.step = step; input.value = value;
    input.addEventListener("input", () => {
      const v = Number(input.value);
      if (!Number.isNaN(v)) { setter(Math.min(max, Math.max(min, v))); onStateChanged(); }
    });
    label.append(input);
    return label;
  }
  buildBackgroundSection.createNumber = createNumber;
  renderBackgrounds();
}

function buildCharacterSection() {
  const body = createSection("キャラクター", "立ち絵・まばたき・表情差分・セリフ", true);

  const noneLabel = el("label");
  const noneCheck = document.createElement("input");
  noneCheck.type = "checkbox";
  noneCheck.addEventListener("change", () => {
    state.characterNone = noneCheck.checked;
    listWrap.style.display = noneCheck.checked ? "none" : "";
    onStateChanged();
  });
  noneLabel.append(noneCheck, document.createTextNode(" キャラを表示しない (キャラなしスキン)"));
  body.append(noneLabel);
  buildCharacterSection.syncNone = () => {
    noneCheck.checked = state.characterNone;
    listWrap.style.display = state.characterNone ? "none" : "";
  };

  const listWrap = el("div");
  body.append(listWrap);
  const list = el("div");
  listWrap.append(el("div", "field-note", "未設定ならデフォルト (ゆかりちゃん) のまま。2人以上いるとタップで切り替わります (表情を一巡してから次のキャラへ)"));
  listWrap.append(list);
  const addBtn = el("button", "btn small add", "+ キャラを追加");
  addBtn.type = "button";
  addBtn.addEventListener("click", () => {
    state.characters.push({ asset: null, scale: 1, talk: "", eyesClosed: null, expressions: [] });
    renderCharacters();
    onStateChanged();
  });
  listWrap.append(addBtn);

  listWrap.append(el("hr"));
  listWrap.append(el("div", "field-note", "予約完了画面のポーズ絵 (任意)。無ければキャラの1枚目が使われます"));
  listWrap.append(registerSlot(createSlot({ title: "予約完了ポーズ", kinds: ["image"], get: () => state.poseComplete, set: (a) => (state.poseComplete = a) })));

  function renderCharacters() {
    list.textContent = "";
    state.characters.forEach((ch, i) => {
      const card = el("div", "item-card");
      const header = el("div", "item-card-header");
      header.append(el("div", "item-title", `キャラ ${i + 1}`));
      const del = el("button", "btn small danger", "削除");
      del.type = "button";
      del.addEventListener("click", () => {
        freeAsset(ch.asset); freeAsset(ch.eyesClosed);
        ch.expressions.forEach((x) => freeAsset(x.asset));
        state.characters.splice(i, 1);
        renderCharacters();
        onStateChanged();
      });
      header.append(del);
      card.append(header);

      card.append(createSlot({ title: "立ち絵 (透過 PNG 推奨)", kinds: ["image"], get: () => ch.asset, set: (a) => (ch.asset = a) }));
      card.append(createSlot({ title: "目閉じ差分 (任意。あるとまばたきします)", kinds: ["image"], get: () => ch.eyesClosed, set: (a) => (ch.eyesClosed = a) }));

      if (i === 0) {
        const scaleRow = el("div", "inline-fields");
        scaleRow.style.marginTop = "8px";
        scaleRow.append(buildBackgroundSection.createNumber("大きさ (0.3〜2)", ch.scale, 0.3, 2, 0.05, (v) => (ch.scale = v)));
        card.append(scaleRow);
      }

      const talkField = el("div", "field");
      talkField.style.marginTop = "8px";
      talkField.append(el("label", null, "このキャラのセリフ (1行に1つ、任意)"));
      const talkArea = document.createElement("textarea");
      talkArea.value = ch.talk;
      talkArea.addEventListener("input", () => { ch.talk = talkArea.value; onStateChanged(); });
      talkField.append(talkArea);
      card.append(talkField);

      // 表情差分
      const exprBlock = el("div", "sub-block");
      exprBlock.append(el("div", "field-note", "表情差分 (任意)。タップで 立ち絵 → 表情 → … と巡回します。立ち絵と同じポーズ・同じ解像度が理想"));
      const exprList = el("div");
      exprBlock.append(exprList);
      const exprAdd = el("button", "btn small add", "+ 表情を追加");
      exprAdd.type = "button";
      exprAdd.addEventListener("click", () => {
        ch.expressions.push({ asset: null, talk: "" });
        renderCharacters();
        onStateChanged();
      });
      exprBlock.append(exprAdd);
      card.append(exprBlock);

      ch.expressions.forEach((expr, j) => {
        const exprCard = el("div", "item-card");
        const eh = el("div", "item-card-header");
        eh.append(el("div", "item-title", `表情 ${j + 1}`));
        const edel = el("button", "btn small danger", "削除");
        edel.type = "button";
        edel.addEventListener("click", () => {
          freeAsset(expr.asset);
          ch.expressions.splice(j, 1);
          renderCharacters();
          onStateChanged();
        });
        eh.append(edel);
        exprCard.append(eh);
        exprCard.append(createSlot({ title: "表情画像", kinds: ["image"], get: () => expr.asset, set: (a) => (expr.asset = a) }));
        const ef = el("div", "field");
        ef.style.marginTop = "6px";
        ef.append(el("label", null, "この表情のときのセリフ (1行に1つ、任意)"));
        const ea = document.createElement("textarea");
        ea.style.minHeight = "44px";
        ea.value = expr.talk;
        ea.addEventListener("input", () => { expr.talk = ea.value; onStateChanged(); });
        ef.append(ea);
        exprCard.append(ef);
        exprList.append(exprCard);
      });
      list.append(card);
    });
  }
  buildCharacterSection.render = renderCharacters;
  renderCharacters();
}

function buildBgmSection() {
  const body = createSection("BGM", "通常 + 昼夜 + 季節×昼夜 (mp3 / ogg / wav)");
  body.append(el("div", "field-note", "無い時間帯は 季節×昼夜 → 昼夜 → 通常 → アプリ標準 の順で選ばれます"));
  body.append(registerSlot(createSlot({ title: "通常 BGM", kinds: ["audio"], get: () => state.bgm, set: (a) => (state.bgm = a) })));
  const grid = el("div", "slot-grid");
  grid.style.marginTop = "10px";
  TIMED_KEYS.forEach(({ key, label }) => {
    grid.append(registerSlot(createSlot({
      title: label, kinds: ["audio"],
      get: () => state.timedBgm[key] || null,
      set: (a) => { if (a) state.timedBgm[key] = a; else delete state.timedBgm[key]; },
    })));
  });
  body.append(grid);
}

function buildSeSection() {
  const body = createSection("効果音", "タップ音などの差し替え (任意)");
  body.append(el("div", "field-note", "設定しなかった音はアプリ標準のまま"));
  const grid = el("div", "slot-grid");
  SE_KEYS.forEach(({ key, label }) => {
    grid.append(registerSlot(createSlot({
      title: label, kinds: ["audio"],
      get: () => state.se[key] || null,
      set: (a) => { if (a) state.se[key] = a; else delete state.se[key]; },
    })));
  });
  body.append(grid);
}

function buildTalkSection() {
  const body = createSection("セリフ", "タップしたときのセリフ (1行に1つ)");
  body.append(el("div", "field-note", "キャラ個別のセリフはキャラクター欄で。ここはスキン全体のセリフです。時間帯セリフは通常セリフと合わせて抽選されます"));
  createTextField(body, "通常セリフ", null, () => state.talk, (v) => (state.talk = v), true);
  createTextField(body, "朝 (5〜11時) のセリフ", null, () => state.talkMorning, (v) => (state.talkMorning = v), true);
  createTextField(body, "夕方 (17〜22時) のセリフ", null, () => state.talkEvening, (v) => (state.talkEvening = v), true);
  createTextField(body, "夜 (22〜翌5時) のセリフ", null, () => state.talkNight, (v) => (state.talkNight = v), true);
}

/* ================= skin.json 生成と zip ================= */

/* 出力ファイル名は役割ベースで自動命名する (日本語ファイル名などのトラブル回避) */
function buildManifestAndFiles() {
  const files = []; // { name, blob }
  const used = new Set();
  function claim(name) {
    // extraFiles との衝突も避ける
    let candidate = name;
    let serial = 2;
    while (used.has(candidate)) {
      const i = name.lastIndexOf(".");
      candidate = name.slice(0, i) + "_" + serial + name.slice(i);
      serial++;
    }
    used.add(candidate);
    return candidate;
  }
  function add(asset, baseName) {
    const name = claim(baseName + "." + extOf(asset.srcName));
    files.push({ name, blob: asset.blob });
    return name;
  }

  const json = {};
  if (state.name.trim()) json.name = state.name.trim();
  if (state.author.trim()) json.author = state.author.trim();
  if (state.description.trim()) json.description = state.description.trim();

  const bgs = state.backgrounds.filter((b) => b.asset);
  if (bgs.length > 0) {
    json.backgrounds = bgs.map((b, i) => {
      const entry = { type: b.asset.kind === "video" ? "video" : "image", file: add(b.asset, "bg_" + (i + 1)) };
      if (b.rotation) entry.rotation = b.rotation;
      if (b.zoom !== 1) entry.zoom = b.zoom;
      if (b.offsetX) entry.offset_x = b.offsetX;
      if (b.offsetY) entry.offset_y = b.offsetY;
      return entry;
    });
  }
  TIMED_KEYS.forEach(({ key }) => {
    const a = state.timedBg[key];
    if (a) json["background_" + key] = { type: a.kind === "video" ? "video" : "image", file: add(a, "bg_" + key) };
  });

  if (state.characterNone) {
    json.character = { type: "none" };
  } else {
    const chars = state.characters.filter((c) => c.asset);
    if (chars.length > 0) {
      json.characters = chars.map((c, i) => {
        const entry = { type: "image", file: add(c.asset, "chara_" + (i + 1)) };
        if (i === 0 && c.scale !== 1) entry.scale = c.scale;
        const talk = linesOf(c.talk);
        if (talk) entry.talk = talk;
        if (c.eyesClosed) entry.eyes_closed = add(c.eyesClosed, "chara_" + (i + 1) + "_blink");
        const exprs = c.expressions.filter((x) => x.asset);
        if (exprs.length > 0) {
          entry.expressions = exprs.map((x, j) => {
            const ex = { file: add(x.asset, "chara_" + (i + 1) + "_expr_" + (j + 1)) };
            const et = linesOf(x.talk);
            if (et) ex.talk = et;
            return ex;
          });
        }
        return entry;
      });
    }
  }
  if (state.poseComplete) json.pose_complete = { type: "image", file: add(state.poseComplete, "pose") };

  if (state.bgm) json.bgm = { type: "audio", file: add(state.bgm, "bgm") };
  TIMED_KEYS.forEach(({ key }) => {
    const a = state.timedBgm[key];
    if (a) json["bgm_" + key] = { type: "audio", file: add(a, "bgm_" + key) };
  });
  SE_KEYS.forEach(({ key }) => {
    const a = state.se[key];
    if (a) json["se_" + key] = { type: "audio", file: add(a, "se_" + key) };
  });

  if (state.record) json.record = { type: "image", file: add(state.record, "record") };

  const talk = linesOf(state.talk);
  if (talk) json.talk = talk;
  const tm = linesOf(state.talkMorning);
  if (tm) json.talk_morning = tm;
  const te = linesOf(state.talkEvening);
  if (te) json.talk_evening = te;
  const tn = linesOf(state.talkNight);
  if (tn) json.talk_night = tn;

  if (state.themePrimary) json.theme = { primary: state.themePrimary };

  // 規約ファイル (skin.json には書かない)
  if (state.splash) { used.add("splash.png"); files.push({ name: "splash.png", blob: state.splash.blob }); }
  if (state.thumbnail) { used.add("thumbnail.png"); files.push({ name: "thumbnail.png", blob: state.thumbnail.blob }); }

  // zip 読み込み時に見つかった未知キー・未参照ファイルは保持する (将来キーとの互換)
  Object.keys(state.extraJson).forEach((k) => {
    if (!(k in json)) json[k] = state.extraJson[k];
  });
  state.extraFiles.forEach((f) => {
    if (!used.has(f.name)) {
      used.add(f.name);
      files.push({ name: f.name, blob: f.blob });
    }
  });

  return { json, files };
}

async function buildZipBlob() {
  const { json, files } = buildManifestAndFiles();
  const zip = new JSZip();
  zip.file("skin.json", JSON.stringify(json, null, 2));
  files.forEach((f) => zip.file(f.name, f.blob));
  return await zip.generateAsync({ type: "blob", compression: "DEFLATE", compressionOptions: { level: 6 } });
}

function suggestZipName() {
  const base = (state.name.trim() || "skin").replace(/[\\/:*?"<>|]/g, "_");
  return base + ".zip";
}

async function downloadZip() {
  const problems = validate();
  const errors = problems.filter((p) => p.error);
  if (errors.length > 0) {
    alert("作成できません:\n" + errors.map((p) => p.text).join("\n"));
    return;
  }
  const blob = await buildZipBlob();
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = suggestZipName();
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 10000);
}

/* ================= zip 読み込み ================= */

const KNOWN_KEYS = new Set([
  "name", "author", "description", "background", "backgrounds", "character", "characters",
  "pose_complete", "bgm", "record", "talk", "talk_morning", "talk_evening", "talk_night",
  "theme", "layout",
]);
TIMED_KEYS.forEach(({ key }) => { KNOWN_KEYS.add("background_" + key); KNOWN_KEYS.add("bgm_" + key); });
SE_KEYS.forEach(({ key }) => KNOWN_KEYS.add("se_" + key));

/*
 * zip 内のファイルを探す。
 * ZIP 仕様のパス区切りは "/" だが、Windows のツールで作った zip は "\" になっていることがある
 * (実際に PowerShell 等で作られた配布スキンで確認)。どちらでも読めるように正規化して突き合わせる。
 */
function findZipEntry(zip, path) {
  const wanted = path.replace(/\\/g, "/").toLowerCase();
  for (const name of Object.keys(zip.files)) {
    const entry = zip.files[name];
    if (entry.dir) continue;
    if (name.replace(/\\/g, "/").toLowerCase() === wanted) return entry;
  }
  return null;
}

async function loadZipFile(file) {
  let zip;
  try {
    zip = await JSZip.loadAsync(file);
  } catch (e) {
    alert("zip を読み込めませんでした: " + e.message);
    return;
  }
  // skin.json をルート → 1階層下の順で探す (アプリの ImportSkin と同じ)
  let root = "";
  let manifest = findZipEntry(zip, "skin.json");
  if (!manifest) {
    for (const path of Object.keys(zip.files)) {
      const m = path.replace(/\\/g, "/").match(/^([^/]+)\/skin\.json$/i);
      if (m) { root = m[1] + "/"; manifest = zip.files[path]; break; }
    }
  }
  if (!manifest) {
    alert("skin.json が入っていない zip です");
    return;
  }
  let json;
  try {
    json = JSON.parse(await manifest.async("string"));
  } catch (e) {
    alert("skin.json が壊れています: " + e.message);
    return;
  }

  resetState();
  const referenced = new Set(["skin.json"]);
  async function assetOf(fileName) {
    if (!fileName) return null;
    const entry = findZipEntry(zip, root + fileName);
    if (!entry) return null;
    referenced.add(fileName);
    const blob = await entry.async("blob");
    return makeAsset(blob, fileName);
  }

  state.name = json.name || "";
  state.author = json.author || "";
  state.description = json.description || "";
  if (json.theme && json.theme.primary) state.themePrimary = json.theme.primary;
  state.talk = (json.talk || []).join("\n");
  state.talkMorning = (json.talk_morning || []).join("\n");
  state.talkEvening = (json.talk_evening || []).join("\n");
  state.talkNight = (json.talk_night || []).join("\n");

  // 背景 (単数 background は配列の先頭に統合。アプリの GetBackgrounds と同じ)
  const bgLayers = [];
  if (json.background && json.background.file) bgLayers.push(json.background);
  (json.backgrounds || []).forEach((l) => { if (l && l.file) bgLayers.push(l); });
  for (const layer of bgLayers) {
    const asset = await assetOf(layer.file);
    if (asset) {
      state.backgrounds.push({
        asset,
        rotation: layer.rotation || 0,
        zoom: layer.zoom || 1,
        offsetX: layer.offset_x || 0,
        offsetY: layer.offset_y || 0,
      });
    }
  }
  for (const { key } of TIMED_KEYS) {
    const layer = json["background_" + key];
    if (layer && layer.file) {
      const asset = await assetOf(layer.file);
      if (asset) state.timedBg[key] = asset;
    }
  }

  // キャラ (単数 character は配列の先頭に統合)
  const charLayers = [];
  if (json.character) {
    if (json.character.type === "none") state.characterNone = true;
    else if (json.character.file) charLayers.push(json.character);
  }
  (json.characters || []).forEach((l) => { if (l && l.file && l.type !== "none") charLayers.push(l); });
  for (const layer of charLayers) {
    const asset = await assetOf(layer.file);
    if (!asset) continue;
    const ch = {
      asset,
      scale: layer.scale || 1,
      talk: (layer.talk || []).join("\n"),
      eyesClosed: await assetOf(layer.eyes_closed),
      expressions: [],
    };
    for (const expr of layer.expressions || []) {
      if (!expr || !expr.file) continue;
      const ea = await assetOf(expr.file);
      if (ea) ch.expressions.push({ asset: ea, talk: (expr.talk || []).join("\n") });
    }
    state.characters.push(ch);
  }
  if (json.pose_complete && json.pose_complete.file) state.poseComplete = await assetOf(json.pose_complete.file);

  if (json.bgm && json.bgm.file) state.bgm = await assetOf(json.bgm.file);
  for (const { key } of TIMED_KEYS) {
    const layer = json["bgm_" + key];
    if (layer && layer.file) {
      const asset = await assetOf(layer.file);
      if (asset) state.timedBgm[key] = asset;
    }
  }
  for (const { key } of SE_KEYS) {
    const layer = json["se_" + key];
    if (layer && layer.file) {
      const asset = await assetOf(layer.file);
      if (asset) state.se[key] = asset;
    }
  }
  if (json.record && json.record.file) state.record = await assetOf(json.record.file);

  // 規約ファイル
  const splashEntry = findZipEntry(zip, root + "splash.png");
  if (splashEntry) { referenced.add("splash.png"); state.splash = makeAsset(await splashEntry.async("blob"), "splash.png"); }
  const thumbEntry = findZipEntry(zip, root + "thumbnail.png");
  if (thumbEntry) { referenced.add("thumbnail.png"); state.thumbnail = makeAsset(await thumbEntry.async("blob"), "thumbnail.png"); }

  // 未知キーと未参照ファイルを保持 (このツールが古くてもデータを失わない)
  Object.keys(json).forEach((k) => {
    if (!KNOWN_KEYS.has(k)) state.extraJson[k] = json[k];
  });
  if (json.layout) state.extraJson.layout = json.layout; // layout はツールでは編集しないが保持する
  for (const path of Object.keys(zip.files)) {
    const entry = zip.files[path];
    if (entry.dir) continue;
    // 参照済みかどうかは "/" に正規化して突き合わせる ("\" 区切りの zip 対策)。
    // 正規化しないとサブフォルダ内のファイルが未参照と誤判定され、二重に保存されてしまう
    const normalized = path.replace(/\\/g, "/");
    if (!normalized.startsWith(root)) continue;
    const name = normalized.slice(root.length);
    if (referenced.has(name)) continue;
    state.extraFiles.push({ name, blob: await entry.async("blob") });
  }

  refreshAllUi();
  onStateChanged();
}

function resetState() {
  state.backgrounds.forEach((b) => freeAsset(b.asset));
  Object.values(state.timedBg).forEach(freeAsset);
  state.characters.forEach((c) => {
    freeAsset(c.asset); freeAsset(c.eyesClosed);
    c.expressions.forEach((x) => freeAsset(x.asset));
  });
  Object.values(state.timedBgm).forEach(freeAsset);
  Object.values(state.se).forEach(freeAsset);
  [state.poseComplete, state.bgm, state.record, state.splash, state.thumbnail].forEach(freeAsset);
  Object.assign(state, {
    name: "", author: "", description: "", themePrimary: null,
    talk: "", talkMorning: "", talkEvening: "", talkNight: "",
    backgrounds: [], timedBg: {}, characterNone: false, characters: [],
    poseComplete: null, bgm: null, timedBgm: {}, se: {},
    record: null, splash: null, thumbnail: null, extraJson: {}, extraFiles: [],
  });
}

function refreshAllUi() {
  // フォームを作り直す (入力値・スロットを state から復元)
  formRoot.textContent = "";
  slotRegistry.length = 0;
  buildBasicSection();
  buildBackgroundSection();
  buildCharacterSection();
  buildBgmSection();
  buildSeSection();
  buildTalkSection();
  buildBasicSection.syncTheme();
  buildCharacterSection.syncNone();
  // テキストフィールドは getter で初期化されるため再構築だけでよい
}

/* ================= バリデーション ================= */

function validate() {
  const problems = []; // { text, error }
  if (!state.name.trim()) problems.push({ text: "スキンの名前が空です (フォルダ名は zip 名から付きますが、名前も入れるのがおすすめ)", error: false });

  const hasBg = state.backgrounds.some((b) => b.asset) || Object.keys(state.timedBg).length > 0;
  const hasChar = !state.characterNone && state.characters.some((c) => c.asset);
  const hasAnything = hasBg || hasChar || state.characterNone || state.bgm
    || Object.keys(state.timedBgm).length > 0 || Object.keys(state.se).length > 0
    || state.record || state.poseComplete || linesOf(state.talk) || state.themePrimary
    || linesOf(state.talkMorning) || linesOf(state.talkEvening) || linesOf(state.talkNight)
    || state.splash || state.thumbnail;
  if (!hasAnything) problems.push({ text: "何も設定されていません", error: true });

  state.backgrounds.forEach((b, i) => {
    if (!b.asset) problems.push({ text: `背景 ${i + 1}: ファイルが未設定です (このまま作ると出力されません)`, error: false });
  });
  state.characters.forEach((c, i) => {
    if (!c.asset) {
      problems.push({ text: `キャラ ${i + 1}: 立ち絵が未設定です (このまま作ると出力されません)`, error: false });
    }
    c.expressions.forEach((x, j) => {
      if (!x.asset) problems.push({ text: `キャラ ${i + 1} の表情 ${j + 1}: 画像が未設定です`, error: false });
    });
    if (!c.asset && (c.eyesClosed || c.expressions.length > 0)) {
      problems.push({ text: `キャラ ${i + 1}: 立ち絵が無いと目閉じ・表情も出力されません`, error: false });
    }
  });

  // 季節だけ指定して昼夜の穴があるケースの気づき支援
  const bgmSeasonal = TIMED_KEYS.filter(({ key }) => key.includes("_")).filter(({ key }) => state.timedBgm[key]);
  if (bgmSeasonal.length > 0 && bgmSeasonal.length < 8 && !state.bgm && !state.timedBgm.day && !state.timedBgm.night) {
    problems.push({ text: "季節BGMが一部だけ指定されています。無い時間帯はアプリ標準の曲になります (通常 BGM を入れると全時間スキンの曲になります)", error: false });
  }
  return problems;
}

function renderValidation() {
  const listEl = document.getElementById("validation-list");
  listEl.textContent = "";
  const problems = validate();
  if (problems.length === 0) {
    const li = el("li", "ok", "問題ありません。zip を作れます");
    listEl.append(li);
    return;
  }
  problems.forEach((p) => listEl.append(el("li", "warn", p.text)));
}

/* ================= プレビュー ================= */

const pvBg = document.getElementById("preview-bg");
const pvBgVideo = document.getElementById("preview-bg-video");
const pvChara = document.getElementById("preview-chara");
const pvBubble = document.getElementById("preview-bubble");
const pvBubbleText = document.getElementById("preview-bubble-text");
const pvEmpty = document.getElementById("preview-empty");
const pvNavbar = document.getElementById("preview-navbar");
const simMonth = document.getElementById("sim-month");
const simHour = document.getElementById("sim-hour");

function visibleCharacters() {
  if (state.characterNone) return [];
  return state.characters.filter((c) => c.asset);
}

function currentPreviewBackground() {
  const timed = resolveTimedBackground(Number(simMonth.value), Number(simHour.value));
  if (timed) return { layer: { rotation: 0, zoom: 1, offsetX: 0, offsetY: 0 }, asset: timed.asset };
  const first = state.backgrounds.find((b) => b.asset);
  if (first) return { layer: first, asset: first.asset };
  return null;
}

function renderPreview() {
  const bg = currentPreviewBackground();
  pvBg.classList.remove("visible");
  pvBgVideo.classList.remove("visible");
  pvBgVideo.pause();
  if (bg) {
    const t = `translate(${bg.layer.offsetX * 100}%, ${bg.layer.offsetY * 100}%) rotate(${bg.layer.rotation}deg) scale(${Math.max(1, bg.layer.zoom) * (bg.layer.rotation % 180 !== 0 ? 1.8 : 1)})`;
    if (bg.asset.kind === "video") {
      if (pvBgVideo.src !== bg.asset.url) pvBgVideo.src = bg.asset.url;
      pvBgVideo.style.transform = t;
      pvBgVideo.classList.add("visible");
      pvBgVideo.play().catch(() => {});
    } else {
      pvBg.style.backgroundImage = `url("${bg.asset.url}")`;
      pvBg.style.transform = t;
      pvBg.classList.add("visible");
    }
  }

  // キャラ
  const chars = visibleCharacters();
  if (pv.charIndex >= chars.length) { pv.charIndex = 0; pv.exprIndex = 0; }
  const ch = chars[pv.charIndex];
  if (ch) {
    const sprite = pv.exprIndex > 0 && ch.expressions.filter((x) => x.asset)[pv.exprIndex - 1]
      ? ch.expressions.filter((x) => x.asset)[pv.exprIndex - 1].asset
      : ch.asset;
    pvChara.src = sprite.url;
    const scale = chars[0].scale || 1;
    pvChara.style.width = 68 * scale + "%";
    pvChara.classList.add("visible");
  } else {
    pvChara.classList.remove("visible");
    pvChara.removeAttribute("src");
  }

  pvEmpty.style.display = bg || ch ? "none" : "";

  // テーマ色 (ナビバーに反映)
  pvNavbar.style.setProperty("--pv-nav-bg", "");
  pvNavbar.style.background = state.themePrimary ? hexWithAlpha(state.themePrimary, 0.85) : "rgba(123, 92, 214, 0.85)";

  // 時計をシミュレーター時刻に合わせる
  const hour = Number(simHour.value);
  document.getElementById("preview-clock-time").textContent = String(hour).padStart(2, "0") + ":00";
  const month = Number(simMonth.value);
  document.getElementById("preview-clock-date").textContent = String(month).padStart(2, "0") + "/01";

  renderSimResult();
  renderValidation();
}

function hexWithAlpha(hex, alpha) {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

/* タップ (アプリの HandleTap と同じ巡回: 表情を一巡 → 次のキャラ) */
pvChara.addEventListener("click", () => {
  const chars = visibleCharacters();
  if (chars.length === 0) return;
  const ch = chars[pv.charIndex];
  const exprCount = ch.expressions.filter((x) => x.asset).length;
  pv.exprIndex++;
  if (pv.exprIndex >= 1 + exprCount) {
    pv.exprIndex = 0;
    pv.charIndex = (pv.charIndex + 1) % chars.length;
  }
  // スクイーズ
  pvChara.style.transition = "none";
  pvChara.animate(
    [{ transform: "translateX(-50%) scale(1.06, 0.92)" }, { transform: "translateX(-50%) scale(1, 1)" }],
    { duration: 140 }
  );
  renderPreview();
  showBubble(pickLine());
});

function pickLine() {
  const chars = visibleCharacters();
  const ch = chars[pv.charIndex];
  if (ch) {
    if (pv.exprIndex > 0) {
      const expr = ch.expressions.filter((x) => x.asset)[pv.exprIndex - 1];
      const lines = expr ? linesOf(expr.talk) : null;
      if (lines) return lines[Math.floor(Math.random() * lines.length)];
    }
    const charLines = linesOf(ch.talk);
    if (charLines) return charLines[Math.floor(Math.random() * charLines.length)];
  }
  const suffix = timeOfDaySuffix(Number(simHour.value));
  const timedText = suffix === "morning" ? state.talkMorning : suffix === "evening" ? state.talkEvening : suffix === "night" ? state.talkNight : "";
  const merged = [...(linesOf(timedText) || []), ...(linesOf(state.talk) || [])];
  if (merged.length > 0) return merged[Math.floor(Math.random() * merged.length)];
  if (chars.length === 0) return DEFAULT_LINES[Math.floor(Math.random() * DEFAULT_LINES.length)]; // デフォルトのゆかりちゃん相当
  return null; // カスタムキャラでセリフ未設定なら無言 (アプリと同じ)
}

function showBubble(line) {
  if (!line) return;
  pvBubbleText.textContent = line;
  pvBubble.hidden = false;
  clearTimeout(pv.bubbleTimer);
  pv.bubbleTimer = setTimeout(() => (pvBubble.hidden = true), 2200);
}

/* まばたき (立ち絵表示中のみ。アプリと同じ 2.5〜6 秒間隔・0.12 秒) */
function scheduleBlink() {
  clearTimeout(pv.blinkTimer);
  pv.blinkTimer = setTimeout(() => {
    const chars = visibleCharacters();
    const ch = chars[pv.charIndex];
    if (ch && ch.eyesClosed && pv.exprIndex === 0 && pvChara.classList.contains("visible")) {
      const back = pvChara.src;
      pvChara.src = ch.eyesClosed.url;
      setTimeout(() => {
        if (pv.exprIndex === 0) pvChara.src = back;
      }, 120);
    }
    scheduleBlink();
  }, 2500 + Math.random() * 3500);
}

/* ================= シミュレーター ================= */

function initSimulator() {
  for (let m = 1; m <= 12; m++) {
    const o = el("option", null, m + "月");
    o.value = m;
    simMonth.append(o);
  }
  for (let h = 0; h < 24; h++) {
    const o = el("option", null, h + "時");
    o.value = h;
    simHour.append(o);
  }
  const now = new Date();
  simMonth.value = now.getMonth() + 1;
  simHour.value = now.getHours();
  simMonth.addEventListener("change", renderPreview);
  simHour.addEventListener("change", renderPreview);
  document.getElementById("sim-now").addEventListener("click", () => {
    const n = new Date();
    simMonth.value = n.getMonth() + 1;
    simHour.value = n.getHours();
    renderPreview();
  });

  const audio = document.getElementById("bgm-audio");
  const playBtn = document.getElementById("btn-bgm-play");
  playBtn.addEventListener("click", () => {
    if (!audio.paused) {
      audio.pause();
      playBtn.textContent = "♪ BGM を試聴";
      return;
    }
    const { asset } = resolveBgm(Number(simMonth.value), Number(simHour.value));
    if (!asset) {
      alert("この時刻はアプリ標準の BGM になります (このスキンの BGM はありません)");
      return;
    }
    audio.src = asset.url;
    audio.play();
    playBtn.textContent = "■ 停止";
  });
}

function renderSimResult() {
  const month = Number(simMonth.value);
  const hour = Number(simHour.value);
  const box = document.getElementById("sim-result");
  const season = { spring: "春", summer: "夏", autumn: "秋", winter: "冬" }[seasonOf(month)];
  const dayNight = isDaytime(hour) ? "昼" : "夜";
  const bgm = resolveBgm(month, hour);
  const timedBg = resolveTimedBackground(month, hour);
  const bgText = timedBg
    ? `${timedBg.asset.srcName} (${timedBg.label})`
    : state.backgrounds.some((b) => b.asset) ? "通常の背景 1枚目" : "(アプリ標準)";
  const suffix = timeOfDaySuffix(hour);
  const talkBand = suffix === "morning" ? "朝" : suffix === "evening" ? "夕方" : suffix === "night" ? "夜" : "通常のみ";
  box.innerHTML = "";
  box.append(
    Object.assign(el("div"), { innerHTML: `<b>${month}月 ${hour}時</b> = ${season}・${dayNight}` }),
    Object.assign(el("div"), { innerHTML: `背景: <b>${escapeHtml(bgText)}</b>` }),
    Object.assign(el("div"), { innerHTML: `BGM: <b>${escapeHtml(bgm.asset ? bgm.asset.srcName + " (" + bgm.label + ")" : "(アプリ標準)")}</b>` }),
    Object.assign(el("div"), { innerHTML: `セリフ帯: <b>${talkBand}</b>` })
  );
}

function escapeHtml(s) {
  return s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

/* ================= 全体イベント ================= */

let renderQueued = false;
function onStateChanged() {
  if (renderQueued) return;
  renderQueued = true;
  requestAnimationFrame(() => {
    renderQueued = false;
    renderPreview();
  });
}

function initGlobal() {
  document.getElementById("btn-build-zip").addEventListener("click", downloadZip);
  const zipInput = document.getElementById("zip-file-input");
  document.getElementById("btn-load-zip").addEventListener("click", () => zipInput.click());
  zipInput.addEventListener("change", () => {
    if (zipInput.files.length > 0) loadZipFile(zipInput.files[0]);
    zipInput.value = "";
  });

  // ページ全体への zip ドロップ
  const overlay = document.getElementById("drop-overlay");
  let dragDepth = 0;
  document.addEventListener("dragenter", (ev) => {
    if ([...(ev.dataTransfer?.items || [])].some((i) => i.kind === "file")) {
      dragDepth++;
      overlay.hidden = false;
    }
  });
  document.addEventListener("dragleave", () => {
    dragDepth = Math.max(0, dragDepth - 1);
    if (dragDepth === 0) overlay.hidden = true;
  });
  document.addEventListener("dragover", (ev) => ev.preventDefault());
  document.addEventListener("drop", (ev) => {
    ev.preventDefault();
    dragDepth = 0;
    overlay.hidden = true;
    const file = ev.dataTransfer.files[0];
    if (file && extOf(file.name) === "zip") loadZipFile(file);
  });
  // スロット上のドロップはスロット側が stopPropagation するため、ここに来るのは枠外のみ
}

/* ================= 起動 ================= */

refreshAllUi();
initSimulator();
initGlobal();
renderPreview();
scheduleBlink();

// 動作検証用フック (コンソールから状態を触れるように。アプリ動作には影響しない)
window.__skinMaker = { state, makeAsset, buildManifestAndFiles, buildZipBlob, loadZipFile, refreshAllUi, onStateChanged, validate };
