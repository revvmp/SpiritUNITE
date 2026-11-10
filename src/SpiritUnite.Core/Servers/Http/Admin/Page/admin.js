"use strict"

const TOKEN_KEY = "spiritunite-admin-token"
const API = "/admin/api"
const app = document.getElementById("app")
const toasts = document.getElementById("toasts")

const EMBLEM = '<svg class="emblem" viewBox="0 0 64 64" aria-hidden="true"><defs><linearGradient id="emblem-fill" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#5b4ce0"/><stop offset="1" stop-color="#12a594"/></linearGradient></defs><circle cx="32" cy="32" r="31" fill="url(#emblem-fill)"/><circle cx="32" cy="32" r="25.5" fill="none" stroke="#ffffff" stroke-width="2.5" opacity=".35"/><path d="M30 12c1.8 11.2 5.8 15.2 17 17-11.2 1.8-15.2 5.8-17 17-1.8-11.2-5.8-15.2-17-17 11.2-1.8 15.2-5.8 17-17Z" fill="#ffffff"/><circle cx="46" cy="46" r="4.6" fill="#ffffff" opacity=".85"/></svg>'
const SEARCH_ICON = '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>'

const VIEWS = [
  ["overview", "Overview"],
  ["players", "Players"],
  ["squads", "Squads"],
  ["reports", "Reports"],
  ["mail", "Mail"],
  ["gift-codes", "Gift Codes"],
  ["settings", "Settings"],
  ["lookup", "Lookup"],
  ["tokens", "Tokens"],
  ["log", "Log"],
]

const KINDS = [
  { key: "coin", label: "Coins", list: "coins" },
  { key: "pokemon", label: "Pokemon", list: "pokemon", unique: true },
  { key: "license", label: "Unite licenses", list: "licenses" },
  { key: "holowear", label: "Holowear", list: "holowear", unique: true },
  { key: "item", label: "Items", list: "items" },
  { key: "outfit", label: "Outfits", list: "outfits", unique: true },
  { key: "icon", label: "Icons", list: "icons", unique: true },
  { key: "frame", label: "Frames", list: "frames", unique: true },
  { key: "backdrop", label: "Backdrops", list: "backdrops", unique: true },
  { key: "emblem", label: "Emblems", list: "emblems" },
]

const MAX_ATTACHMENTS = 10
const GIVE_ALL = [
  ["pokemon", "Pokemon"],
  ["holowear", "Holowear"],
  ["outfit", "Outfits"],
  ["icon", "Icons"],
  ["frame", "Frames"],
  ["backdrop", "Backdrops"],
  ["emblem", "Emblems"],
]
const MAX_OFFICIAL_TEXT = 60

let catalog = null
let adminName = ""
let lastSearch = ""
let lastPage = 0

function h(tag, props, ...children) {
  const element = document.createElement(tag)
  for (const [name, value] of Object.entries(props || {})) {
    if (value === null || value === undefined || value === false) continue
    if (name === "class") element.className = value
    else if (name.startsWith("on")) element.addEventListener(name.slice(2), value)
    else if (name === "value") element.value = value
    else if (name === "checked") element.checked = value
    else element.setAttribute(name, value === true ? "" : value)
  }
  appendAll(element, children)
  return element
}

function appendAll(element, children) {
  for (const child of children.flat(Infinity)) {
    if (child === null || child === undefined || child === false) continue
    element.append(child instanceof Node ? child : String(child))
  }
}

function fill(element, ...children) {
  element.replaceChildren()
  appendAll(element, children)
}

function svg(markup) {
  const holder = document.createElement("span")
  holder.innerHTML = markup
  return holder.firstElementChild
}

function logo() {
  return h("a", { class: "logo", href: "#overview", "aria-label": "SpiritUNITE" },
    svg(EMBLEM),
    h("span", { class: "wordmark", "aria-hidden": "true" }, h("span", { class: "wordmark-top" }, "Spirit"), h("span", { class: "wordmark-bottom" }, "Unite")))
}

function readToken() {
  try { return sessionStorage.getItem(TOKEN_KEY) || "" } catch { return "" }
}

function writeToken(token) {
  try {
    if (token) sessionStorage.setItem(TOKEN_KEY, token)
    else sessionStorage.removeItem(TOKEN_KEY)
  } catch { }
}

let memoryToken = readToken()

async function api(method, path, body) {
  const headers = { Authorization: "Bearer " + memoryToken }
  if (body !== undefined) headers["Content-Type"] = "application/json"
  const response = await fetch(API + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  const data = await response.json().catch(() => null)
  if (response.status === 401) {
    if (mainElement) signOut()
    throw new Error("Session ended")
  }
  if (!response.ok) throw new Error(data && data.error ? data.error : "Request failed")
  return data
}

function toast(text, bad) {
  const item = h("div", { class: bad ? "toast bad" : "toast", role: "status" }, text)
  toasts.append(item)
  setTimeout(() => item.remove(), 3800)
}

function fail(error) {
  toast(error.message || "Something went wrong", true)
}

function modal({ title, text, confirm, danger, body }) {
  return new Promise(resolve => {
    const close = value => {
      back.remove()
      document.removeEventListener("keydown", onKey)
      resolve(value)
    }
    const onKey = event => { if (event.key === "Escape") close(false) }
    const okButton = h("button", { class: danger ? "button danger" : "button primary", type: "submit" }, confirm || "Confirm")
    const form = h("form", { class: "modal", onsubmit: event => { event.preventDefault(); close(true) } },
      h("h2", null, title),
      text ? h("p", null, text) : null,
      body || null,
      h("div", { class: "actions" },
        h("button", { class: "button ghost", type: "button", onclick: () => close(false) }, "Cancel"),
        okButton))
    const back = h("div", { class: "modal-back", onclick: event => { if (event.target === back) close(false) } }, form)
    document.addEventListener("keydown", onKey)
    document.body.append(back)
    const firstInput = form.querySelector("input, select, textarea")
    ;(firstInput || okButton).focus()
  })
}

function formatNumber(value) {
  return Number(value || 0).toLocaleString()
}

function formatTime(seconds) {
  if (!seconds) return "Never"
  return new Date(seconds * 1000).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" })
}

function formatSince(seconds) {
  const elapsed = Math.max(0, Math.floor(Date.now() / 1000) - seconds)
  const days = Math.floor(elapsed / 86400)
  const hours = Math.floor((elapsed % 86400) / 3600)
  const minutes = Math.floor((elapsed % 3600) / 60)
  if (days > 0) return days + "d " + hours + "h"
  if (hours > 0) return hours + "h " + minutes + "m"
  return minutes + "m"
}

function catalogName(list, id, fallback) {
  const entry = catalog && catalog[list].find(item => item.id === id)
  return entry ? entry.name : fallback
}

function pokemonName(id) {
  return catalogName("pokemon", id, "Pokemon " + id)
}

function humanize(name) {
  const words = name
    .replace(/IDs/g, "Ids")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .split(" ")
  return words.map((word, index) => {
    if (word.length > 1 && word === word.toUpperCase()) return word
    const lower = word.toLowerCase()
    return index === 0 ? lower.charAt(0).toUpperCase() + lower.slice(1) : lower
  }).join(" ")
}


function matchesQuery(entry, query) {
  if (!query) return true
  const label = (entry.label || entry.name).toLowerCase()
  return query.split(/\s+/).every(word => label.includes(word) || entry.type.toLowerCase().includes(word) || String(entry.id).includes(word))
}

function coinName(id) {
  return catalogName("coins", id, "Coin " + id)
}

async function loadCatalog() {
  if (!catalog) catalog = await api("GET", "/catalog")
  return catalog
}

function signOut() {
  mainElement = null
  memoryToken = ""
  writeToken("")
  catalog = null
  sharedNames.clear()
  showLogin()
}

function showLogin(message) {
  const input = h("input", { type: "password", placeholder: "Admin token", autocomplete: "off", spellcheck: "false", required: true, "aria-label": "Admin token" })
  const error = h("p", { class: "error", role: "alert", hidden: !message }, message || "")
  const button = h("button", { class: "button primary", type: "submit" }, "Log in")
  const form = h("form", {
    class: "login-card",
    onsubmit: async event => {
      event.preventDefault()
      button.disabled = true
      memoryToken = input.value.trim()
      try {
        await start()
        writeToken(memoryToken)
      } catch {
        memoryToken = ""
        error.textContent = "That token is not valid"
        error.hidden = false
        button.disabled = false
      }
    },
  }, logo(), h("h1", null, "Admin"), input, button, error)
  fill(app, h("div", { class: "login" }, form))
  input.focus()
}

async function start() {
  const me = await api("GET", "/me")
  adminName = me.name
  renderShell()
  if (!routing) window.addEventListener("hashchange", route)
  routing = true
  route()
}

let mainElement = null
let navLinks = {}
let routing = false

function renderShell() {
  navLinks = {}
  const nav = h("nav", { class: "nav" }, VIEWS.map(([key, label]) => {
    navLinks[key] = h("a", { href: "#" + key }, label)
    return navLinks[key]
  }))
  mainElement = h("main")
  fill(app, 
    h("header", { class: "topbar" },
      h("div", { class: "topbar-inner" },
        logo(),
        nav,
        h("div", { class: "who" }, h("span", null, adminName), h("button", { class: "button small", type: "button", onclick: signOut }, "Log out")))),
    mainElement)
}

function route() {
  if (!mainElement) return
  const [view, argument] = location.hash.slice(1).split("/")
  const current = VIEWS.some(([key]) => key === view) ? view : "overview"
  for (const [key, link] of Object.entries(navLinks)) link.classList.toggle("active", key === current)
  const page = h("div")
  fill(mainElement, page)
  const render = { overview: overviewView, players: playersView, squads: squadsView, reports: reportsView, mail: mailView, settings: settingsView, lookup: lookupView, "gift-codes": giftCodesView, tokens: tokensView, log: logView }[current]
  render(page, argument ? decodeURIComponent(argument) : "").catch(fail)
}

function pageHead(title, ...extra) {
  return h("div", { class: "page-head" }, typeof title === "string" ? h("h1", null, title) : title, h("div", { class: "actions" }, extra))
}

function card(title, body, ...headExtra) {
  return h("section", { class: "card" },
    title ? h("div", { class: "card-head" }, h("h2", null, title), h("div", { class: "actions" }, headExtra)) : null,
    body)
}

function playerTable(rows, emptyText) {
  if (rows.length === 0) return h("div", { class: "empty" }, emptyText)
  return h("table", null,
    h("thead", null, h("tr", null, h("th", null, "Trainer"), h("th", null, "Player ID"), h("th", { class: "hide-small" }, "Account"), h("th", null, "Status"))),
    h("tbody", null, rows.map(row => h("tr", { class: "link", onclick: () => { location.hash = "#players/" + row.uid } },
      h("td", null, row.name || h("span", { class: "muted" }, "No trainer")),
      h("td", { class: "mono" }, row.shortId),
      h("td", { class: "hide-small" }, row.isGuest ? h("span", { class: "muted" }, "Guest") : row.userName || ""),
      h("td", null, h("span", { class: row.online ? "dot on" : "dot" }), row.online ? "Online" : "Offline")))))
}

function trainerLink(trainer) {
  if (!trainer || !trainer.uid) return h("span", { class: "muted" }, "None")
  return h("a", { href: "#players/" + trainer.uid, onclick: event => event.stopPropagation() }, trainer.name || String(trainer.uid))
}

function trainerList(trainers) {
  if (trainers.length === 0) return h("span", { class: "muted" }, "None")
  return h("div", { class: "chips" }, trainers.map(trainer => h("span", { class: "chip" }, trainerLink(trainer))))
}

function battlesTable(battles) {
  if (battles.length === 0) return h("div", { class: "empty" }, "No battles right now")
  return h("table", null,
    h("thead", null, h("tr", null, h("th", null, "Map"), h("th", null, "Stage"), h("th", null, "Players"), h("th", { class: "hide-small" }, "Bots"))),
    h("tbody", null, battles.map(battle => h("tr", null,
      h("td", null, battle.map || h("span", { class: "muted" }, "Unknown")),
      h("td", null, h("span", { class: battle.phase === "In battle" ? "badge green" : "badge" }, battle.phase)),
      h("td", null, trainerList(battle.players)),
      h("td", { class: "num hide-small" }, battle.bots)))))
}

function roomsTable(rooms) {
  if (rooms.length === 0) return h("div", { class: "empty" }, "No rooms right now")
  return h("table", null,
    h("thead", null, h("tr", null, h("th", null, "Room"), h("th", null, "Type"), h("th", null, "Map"), h("th", null, "State"), h("th", null, "Members"))),
    h("tbody", null, rooms.map(room => h("tr", null,
      h("td", { class: "mono" }, room.roomShowId),
      h("td", null, room.roomType),
      h("td", null, room.map || h("span", { class: "muted" }, "None")),
      h("td", null, room.state, room.matchStartedAt ? h("span", { class: "muted" }, " for " + formatSince(room.matchStartedAt)) : null),
      h("td", null, trainerList(room.members), room.bots ? h("span", { class: "muted" }, " and " + room.bots + " bots") : null)))))
}

async function overviewView(page) {
  const [overview, online, server] = await Promise.all([api("GET", "/overview"), api("GET", "/online"), api("GET", "/server")])
  const stat = (label, value) => h("div", { class: "stat" }, h("div", { class: "stat-label" }, label), h("div", { class: "stat-value" }, value))
  fill(page, 
    pageHead("Overview", h("button", { class: "button", type: "button", onclick: route }, "Refresh")),
    h("div", { class: "stats" },
      stat("Online", formatNumber(overview.online)),
      stat("In battle", formatNumber(overview.inBattle)),
      stat("Trainers", formatNumber(overview.trainers)),
      stat("Accounts", formatNumber(overview.accounts)),
      stat("Uptime", formatSince(overview.startTime))),
    card("Online now", h("div", { class: "card-body flush" }, playerTable(online, "Nobody is online")), h("span", { class: "badge" }, online.length)),
    card("Battles", h("div", { class: "card-body flush" }, battlesTable(server.battles)), h("span", { class: "badge" }, server.battles.length)),
    card("Rooms", h("div", { class: "card-body flush" }, roomsTable(server.rooms)), h("span", { class: "badge" }, server.rooms.length)))
}

async function playersView(page, argument) {
  if (argument) return playerDetailView(page, argument)
  const results = h("div", { class: "card-body flush" })
  const count = h("span", { class: "badge" })
  const pager = h("div", { class: "pager" })
  let timer = 0

  const load = async (query, pageNumber) => {
    lastSearch = query
    lastPage = pageNumber
    const trimmed = query.trim()
    const path = trimmed ? "/players?q=" + encodeURIComponent(trimmed) : "/players?page=" + pageNumber
    const result = await api("GET", path)
    if (lastSearch !== query) return
    fill(results, playerTable(result.players, trimmed ? "No players found" : "No accounts yet"))
    count.textContent = formatNumber(result.total)
    const pages = Math.max(1, Math.ceil(result.total / result.pageSize))
    const paged = !trimmed && pages > 1
    pager.hidden = !paged
    if (paged) {
      fill(pager,
        h("button", { class: "button small", type: "button", disabled: pageNumber === 0, onclick: () => load(query, pageNumber - 1).catch(fail) }, "Previous"),
        h("span", { class: "muted" }, "Page " + (pageNumber + 1) + " of " + pages),
        h("button", { class: "button small", type: "button", disabled: pageNumber + 1 >= pages, onclick: () => load(query, pageNumber + 1).catch(fail) }, "Next"))
    }
  }

  const input = h("input", {
    type: "search",
    placeholder: "Search players",
    value: lastSearch,
    "aria-label": "Search players",
    oninput: event => {
      clearTimeout(timer)
      timer = setTimeout(() => load(event.target.value, 0).catch(fail), 250)
    },
  })
  fill(page,
    pageHead("Players"),
    h("div", { class: "search spaced" }, svg(SEARCH_ICON), input),
    card("Accounts", results, count),
    pager)
  input.focus()
  await load(lastSearch, lastSearch ? 0 : lastPage)
}

async function playerDetailView(page, uid) {
  await loadCatalog()
  let player = await api("GET", "/players/" + encodeURIComponent(uid))
  const refresh = updated => {
    player = updated
    draw()
  }

  const kick = async () => {
    if (!await modal({ title: "Kick " + displayName(player) + "?", text: "They are disconnected from the game.", confirm: "Kick", danger: true })) return
    await api("POST", "/players/" + uid + "/kick")
    toast("Kicked")
    refresh(await api("GET", "/players/" + uid))
  }

  const ban = async () => {
    const days = h("select", null,
      [["1", "1 day"], ["3", "3 days"], ["7", "7 days"], ["30", "30 days"], ["0", "Permanent"]].map(([value, label]) => h("option", { value }, label)))
    const reason = h("input", { maxlength: "200", placeholder: "Optional" })
    const body = h("div", null, h("label", { class: "field" }, h("span", null, "Length"), days), h("label", { class: "field" }, h("span", null, "Reason"), reason))
    if (!await modal({ title: "Ban " + displayName(player) + "?", text: "They cannot log in until the ban ends.", confirm: "Ban", danger: true, body })) return
    refresh(await api("POST", "/players/" + uid + "/ban", { days: Number(days.value), reason: reason.value }))
    toast("Banned")
  }

  const unban = async () => {
    refresh(await api("POST", "/players/" + uid + "/unban"))
    toast("Unbanned")
  }

  const giveAll = async (category, label) => {
    if (!await modal({ title: "Give all " + label.toLowerCase() + " to " + displayName(player) + "?", text: "This cannot be undone.", confirm: "Give all" })) return
    const result = await api("POST", "/players/" + uid + "/give-all/" + category)
    toast(result.granted ? "Gave " + formatNumber(result.granted) + " " + label.toLowerCase() : "They already own every one")
    refresh(await api("GET", "/players/" + uid))
  }

  const rename = async () => {
    const name = h("input", { maxlength: "40", value: player.name })
    const body = h("div", null, h("label", { class: "field" }, h("span", null, "New name"), name))
    if (!await modal({ title: "Rename " + displayName(player) + "?", text: "Online players see it after they log in again.", confirm: "Rename", body })) return
    refresh(await api("POST", "/players/" + uid + "/rename", { name: name.value }))
    toast("Renamed")
  }

  const draw = () => {
    const name = displayName(player)
    const badges = [
      player.online ? h("span", { class: "badge green" }, "Online") : h("span", { class: "badge" }, "Offline"),
      player.inBattle ? h("span", { class: "badge green" }, "In battle") : null,
      player.ban ? h("span", { class: "badge red" }, player.ban.banEndTime ? "Banned until " + formatTime(player.ban.banEndTime) : "Banned") : null,
      player.isGuest ? h("span", { class: "badge" }, "Guest") : null,
    ]
    const head = h("div", { class: "profile" },
      h("div", { class: "avatar", "aria-hidden": "true" }, name.charAt(0).toUpperCase()),
      h("div", null, h("h1", null, name), h("div", { class: "row" }, badges)))
    const fact = (label, value) => h("div", null, h("div", { class: "fact-label" }, label), h("div", { class: "fact-value" }, value))
    const profile = card("Profile", h("div", { class: "card-body facts" },
      fact("Level", player.name ? formatNumber(player.level) : "None"),
      fact("Exp", formatNumber(player.exp)),
      fact("Credit", player.name ? player.creditPoint : "None"),
      fact("Player ID", player.shortId || "None"),
      fact("Uid", player.uid),
      fact("Account", player.isGuest ? "Guest" : player.userName || "None"),
      fact("Email", player.email || "None"),
      fact("Last login", formatTime(player.lastLoginTime)),
      fact("Joined", formatTime(player.createTime)),
      player.battleBanEndTime ? fact("Match ban until", formatTime(player.battleBanEndTime)) : null))
    const coins = card("Coins", h("div", { class: "card-body" },
      player.coins.length === 0 ? h("p", { class: "muted" }, "No coins")
        : h("div", { class: "coins" }, player.coins.map(coin => h("div", { class: "coin" }, h("span", null, coinName(coin.coinId)), h("strong", { class: "num" }, formatNumber(coin.amount)))))))
    const pokemon = card("Pokemon", h("div", { class: "card-body" },
      player.pokemon.length === 0 ? h("p", { class: "muted" }, "No Pokemon")
        : h("div", { class: "chips" }, player.pokemon.map(id => h("span", { class: "chip", title: String(id) }, pokemonName(id))))),
    h("span", { class: "badge" }, player.pokemon.length))

    const actions = [
      player.name ? h("button", { class: "button", type: "button", onclick: () => rename().catch(fail) }, "Rename") : null,
      player.online || player.inBattle ? h("button", { class: "button", type: "button", onclick: () => kick().catch(fail) }, "Kick") : null,
      player.ban
        ? h("button", { class: "button", type: "button", onclick: () => unban().catch(fail) }, "Unban")
        : h("button", { class: "button danger", type: "button", onclick: () => ban().catch(fail) }, "Ban"),
    ]
    const right = []
    if (player.name) {
      right.push(card("Send gift", composer({
        sendLabel: "Send",
        onSend: async draft => {
          await api("POST", "/players/" + uid + "/mail", draft)
          toast("Sent to " + name)
        },
      })))
      right.push(card("Give all", h("div", { class: "card-body" },
        h("p", { class: "section-intro" }, "Adds every released one straight to the account. Owned ones are skipped."),
        h("div", { class: "row" }, GIVE_ALL.map(([category, label]) => h("button", {
          class: "button", type: "button",
          onclick: () => giveAll(category, label).catch(fail),
        }, label))))))
      const credit = h("input", { type: "number", min: "0", max: "100", value: player.creditPoint, "aria-label": "Credit" })
      right.push(card("Credit", h("div", { class: "card-body" },
        h("div", { class: "row" },
          h("div", { class: "grow" }, credit),
          h("button", {
            class: "button", type: "button",
            onclick: () => api("POST", "/players/" + uid + "/credit", { creditPoint: Number(credit.value) }).then(updated => { refresh(updated); toast("Credit set") }).catch(fail),
          }, "Set"),
          player.battleBanEndTime ? h("button", {
            class: "button", type: "button",
            onclick: () => api("POST", "/players/" + uid + "/clear-battle-ban").then(updated => { refresh(updated); toast("Match ban cleared") }).catch(fail),
          }, "Clear match ban") : null))))
    }
    fill(page, 
      h("div", { class: "page-head" }, h("div", null, h("a", { class: "back", href: "#players" }, "Players"), head), h("div", { class: "actions" }, actions)),
      h("div", { class: "grid-2" }, h("div", null, profile, coins, pokemon), h("div", null, right.length ? right : card(null, h("div", { class: "empty" }, "This account has no trainer yet")))))
  }
  draw()
}

function displayName(player) {
  return player.name || player.userName || "Player " + player.uid
}

function rewardPicker(label) {
  const attachments = []
  const chips = h("div", { class: "chips" })
  const kindSelect = h("select", { "aria-label": "Type" }, KINDS.map(kind => h("option", { value: kind.key }, kind.label)))
  const count = h("input", { type: "number", min: "1", max: "999999", value: "1", "aria-label": "Amount", class: "amount" })
  const picker = itemPicker(() => kindEntries(kindSelect.value))

  const kindOf = key => KINDS.find(kind => kind.key === key)
  const syncCount = () => {
    const unique = kindOf(kindSelect.value).unique
    count.disabled = unique
    if (unique) count.value = "1"
  }
  kindSelect.addEventListener("change", () => {
    picker.clear()
    syncCount()
  })

  const drawChips = () => {
    fill(chips, ...attachments.map((attachment, index) => h("span", { class: "chip" },
      attachment.label + (attachment.count > 1 ? " x" + formatNumber(attachment.count) : ""),
      h("button", { type: "button", "aria-label": "Remove", onclick: () => { attachments.splice(index, 1); drawChips() } }, "×"))))
  }

  const add = () => {
    const selected = picker.selected()
    const amount = Math.floor(Number(count.value))
    if (!selected) return toast("Pick an item first", true)
    if (!(amount >= 1 && amount <= 999999)) return toast("Amount must be 1 to 999,999", true)
    const existing = attachments.find(attachment => attachment.kind === kindSelect.value && attachment.id === selected.id)
    if (existing && kindOf(kindSelect.value).unique) return toast("Already added", true)
    if (!existing && attachments.length >= MAX_ATTACHMENTS) return toast("At most " + MAX_ATTACHMENTS + " attachments", true)
    if (existing) existing.count += amount
    else attachments.push({ kind: kindSelect.value, id: selected.id, count: amount, label: selected.label })
    picker.clear()
    syncCount()
    drawChips()
  }

  syncCount()
  return {
    element: h("div", { class: "field" }, h("span", null, label),
      h("div", { class: "row" },
        h("div", { class: "kind" }, kindSelect),
        h("div", { class: "grow" }, picker.element),
        count,
        h("button", { class: "button", type: "button", onclick: add }, "Add")),
      h("div", { class: "attachments" }, chips)),
    list: () => attachments.map(({ kind, id, count }) => ({ kind, id, count })),
    clear: () => { attachments.length = 0; drawChips() },
  }
}

function composer({ onSend, sendLabel, withAudience }) {
  const subject = h("input", { maxlength: "64", value: "Gift", required: true })
  const content = h("textarea", { rows: "3", maxlength: "400", placeholder: "Optional" })
  const rewards = rewardPicker("Attachments")
  const audience = withAudience ? h("input", { type: "checkbox" }) : null

  const sendButton = h("button", { class: "button primary", type: "submit" }, sendLabel)
  const form = h("form", {
    onsubmit: async event => {
      event.preventDefault()
      sendButton.disabled = true
      try {
        const sent = await onSend({
          subject: subject.value,
          content: content.value,
          attachments: rewards.list(),
          onlineOnly: audience ? audience.checked : false,
        })
        if (sent === false) return
        rewards.clear()
        content.value = ""
      } catch (error) {
        fail(error)
      } finally {
        sendButton.disabled = false
      }
    },
  },
  h("div", { class: "card-body" },
    h("label", { class: "field" }, h("span", null, "Subject"), subject),
    h("label", { class: "field" }, h("span", null, "Message"), content),
    rewards.element),
  h("div", { class: "form-footer" },
    audience ? h("label", { class: "check push-left" }, audience, "Online players only") : null,
    sendButton))
  return form
}

function kindEntries(kindKey) {
  return listEntries(KINDS.find(item => item.key === kindKey).list)
}

function listEntries(listName) {
  return (catalog[listName] || []).map(entry => ({ id: entry.id, label: entry.name, type: entry.type }))
}

function itemPicker(entries, onChoose, ariaLabel) {
  let chosen = null
  let matches = []
  let activeIndex = 0
  const input = h("input", { placeholder: "Search by name", autocomplete: "off", "aria-label": ariaLabel || "Item" })
  const list = h("div", { class: "picker-list", hidden: true })
  const element = h("div", { class: "picker" }, input, list)

  const show = entry => {
    chosen = entry
    input.value = entry.label
  }

  const choose = entry => {
    list.hidden = true
    if (onChoose) {
      onChoose(entry)
      chosen = null
      input.value = ""
      return
    }
    show(entry)
  }

  const draw = () => {
    const query = input.value.trim().toLowerCase()
    matches = entries().filter(entry => matchesQuery(entry, query)).slice(0, 60)
    activeIndex = 0
    fill(list, ...(matches.length ? matches.map((entry, index) => h("button", {
      type: "button",
      class: index === activeIndex ? "active" : null,
      onmousedown: event => { event.preventDefault(); choose(entry) },
    }, h("span", null, entry.label, entry.type !== entry.label ? h("span", { class: "muted" }, " · " + entry.type) : null), h("span", { class: "muted mono" }, entry.id))) : [h("div", { class: "empty" }, "No matches")]))
    list.hidden = false
  }

  input.addEventListener("input", () => { chosen = null; draw() })
  input.addEventListener("focus", draw)
  input.addEventListener("blur", () => { list.hidden = true })
  input.addEventListener("keydown", event => {
    if (list.hidden || matches.length === 0) return
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault()
      activeIndex = (activeIndex + (event.key === "ArrowDown" ? 1 : matches.length - 1)) % matches.length
      list.querySelectorAll("button").forEach((button, index) => button.classList.toggle("active", index === activeIndex))
      list.children[activeIndex].scrollIntoView({ block: "nearest" })
    } else if (event.key === "Enter") {
      event.preventDefault()
      choose(matches[activeIndex])
    } else if (event.key === "Escape") {
      list.hidden = true
    }
  })

  return {
    element,
    selected: () => chosen,
    show,
    clear: () => { chosen = null; input.value = ""; list.hidden = true },
  }
}

async function mailView(page) {
  await loadCatalog()
  fill(page, 
    pageHead("Mail"),
    h("div", { class: "narrow" }, card("Send to everyone", composer({
      sendLabel: "Send",
      withAudience: true,
      onSend: async draft => {
        const who = draft.onlineOnly ? "every online player" : "every player"
        if (!await modal({ title: "Send this mail?", text: "It goes to " + who + ".", confirm: "Send" })) return false
        const result = await api("POST", "/mail", draft)
        toast("Sent to " + formatNumber(result.sent) + " players")
      },
    }))))
}

const ACTIVATION_CODES_SECTION = { name: "ActivationCodes", group: "Content", title: "Activation Codes", panel: activationCodesPanel }

async function settingsView(page, argument) {
  await loadCatalog()
  const loaded = await api("GET", "/settings")
  const contentCount = loaded.filter(section => section.group === ACTIVATION_CODES_SECTION.group).length
  const sections = [...loaded.slice(0, contentCount), ACTIVATION_CODES_SECTION, ...loaded.slice(contentCount)]
  let current = sections.find(section => section.name === argument) || sections[0]
  const side = h("nav", { class: "side-nav" })
  const body = h("div")

  const drawSide = () => fill(side, ...sections.flatMap((section, index) => [
    index === 0 || sections[index - 1].group !== section.group ? h("div", { class: "side-group" }, section.group) : null,
    h("a", {
      href: "#settings/" + encodeURIComponent(section.name),
      class: section === current ? "active" : null,
    }, section.title, section.restartNeeded ? h("span", { class: "badge amber" }, "Restart") : null),
  ]))

  const drawBody = () => {
    if (current.panel) return current.panel().then(panel => fill(body, panel)).catch(fail)
    const editor = objectEditor(current.schema.fields, current.values)
    const save = h("button", { class: "button primary", type: "submit" }, "Save")
    const form = h("form", {
      onsubmit: async event => {
        event.preventDefault()
        let values
        try {
          values = editor.read()
        } catch (error) {
          return fail(error)
        }
        save.disabled = true
        try {
          const saved = await api("PUT", "/settings/" + encodeURIComponent(current.name), values)
          const index = sections.indexOf(current)
          sections[index] = saved
          current = saved
          drawSide()
          drawBody()
          toast(saved.restartNeeded ? "Saved. Restart the server to apply" : "Saved and live")
        } catch (error) {
          fail(error)
        } finally {
          save.disabled = false
        }
      },
    },
    card(current.title, h("div", null, h("div", { class: "card-body" }, h("p", { class: "section-intro" }, current.description), editor.element), h("div", { class: "form-footer" }, save))))
    fill(body, current.restartNeeded ? h("div", { class: "notice" }, "gamesettings.json was changed outside the panel. Restart the server to apply it") : null, form)
  }

  drawSide()
  drawBody()
  fill(page, pageHead("Settings"), h("div", { class: "grid-side" }, side, body))
}

const SELECT_LIMIT = 40
const COMPACT_FIELD_LIMIT = 3
const OFFICIAL_NAMES_SHOWN = 4

function objectEditor(fields, values) {
  const editors = fields.map(field => ({
    name: field.name,
    editor: valueEditor(field.schema, values ? values[field.name] : undefined, field.label || humanize(field.name), fieldHint(field), field),
  }))
  return {
    element: h("div", null, editors.map(item => item.editor.element)),
    read: () => Object.fromEntries(editors.map(item => [item.name, item.editor.read()])),
  }
}

function entryName(listName, id) {
  const entry = (catalog[listName] || []).find(item => item.id === id)
  return entry ? entry.name : "Unknown (" + id + ")"
}

const sharedNames = new Map()

function sharedKeys(listName) {
  if (!sharedNames.has(listName)) {
    const counts = new Map()
    const count = key => counts.set(key, (counts.get(key) || 0) + 1)
    for (const entry of catalog[listName] || []) {
      count("name:" + entry.name)
      count("type:" + entry.name + "|" + entry.type)
    }
    sharedNames.set(listName, new Set([...counts].filter(([, total]) => total > 1).map(([key]) => key)))
  }
  return sharedNames.get(listName)
}

function chipLabel(listName, id) {
  const entry = (catalog[listName] || []).find(item => item.id === id)
  if (!entry) return "Unknown (" + id + ")"
  const shared = sharedKeys(listName)
  if (!shared.has("name:" + entry.name)) return entry.name
  const detail = shared.has("type:" + entry.name + "|" + entry.type) ? "No. " + entry.id : entry.type
  return [entry.name, h("span", { class: "muted" }, " · " + detail)]
}

function choiceLabel(choices, value) {
  const choice = choices.find(item => item.value === String(value))
  return choice ? choice.label : String(value)
}

function namesText(listName, ids) {
  const names = ids.map(id => entryName(listName, id))
  const shown = names.slice(0, OFFICIAL_NAMES_SHOWN).join(", ")
  return names.length > OFFICIAL_NAMES_SHOWN ? shown + " and " + (names.length - OFFICIAL_NAMES_SHOWN) + " more" : shown
}

function officialText(official, field) {
  if (official === null || official === undefined || official === "") return null
  if (field && field.keyPick) return null
  if (field && field.choices) return choiceLabel(field.choices, official)
  if (field && field.pick) {
    if (Array.isArray(official)) return official.length ? namesText(field.pick, official) : "None"
    return entryName(field.pick, official)
  }
  if (field && field.slots && Array.isArray(official)) {
    const slots = catalog[field.slots] || []
    return official.map((value, index) => (slots[index] ? slots[index].name : "#" + (index + 1)) + " " + value).join(", ")
  }
  if (typeof official === "boolean") return official ? "On" : "Off"
  if (Array.isArray(official)) {
    if (official.length === 0 || official.some(item => item !== null && typeof item === "object")) return null
    return official.join(", ")
  }
  if (typeof official === "object") {
    const text = JSON.stringify(official)
    return Object.keys(official).length && text.length <= MAX_OFFICIAL_TEXT ? text : null
  }
  return String(official)
}

function fieldHint(field) {
  const official = officialText(field.official, field)
  if (!field.help && !official) return null
  return h("span", { class: "hint" }, field.help || "", official ? h("span", { class: "official" }, "Official: " + official) : null)
}

function isEmptyValue(value) {
  return value === null || value === undefined || value === "" || value === 0
}

function choiceSelect(choices, value, label) {
  const known = choices.some(choice => choice.value === String(value))
  const keepOther = !known && !isEmptyValue(value)
  const select = h("select", { "aria-label": label },
    choices.map(choice => h("option", { value: choice.value }, choice.label)),
    keepOther ? h("option", { value: String(value) }, "Other (" + value + ")") : null)
  select.value = known || keepOther ? String(value) : choices.length ? choices[0].value : ""
  return select
}

function entrySelect(listName, value, label) {
  const choices = listEntries(listName).map(entry => ({ value: String(entry.id), label: entry.label }))
  return choiceSelect(choices, value, label)
}

function isShortList(listName) {
  return (catalog[listName] || []).length <= SELECT_LIMIT
}

function numberInput(value, label) {
  return h("input", { type: "number", step: "any", value: value === null || value === undefined ? "" : String(value), "aria-label": label })
}

function readNumber(input, label, nullable) {
  if (input.value.trim() === "") {
    if (nullable) return null
    throw new Error(label + " needs a number")
  }
  const number = Number(input.value)
  if (!Number.isFinite(number)) throw new Error(label + " needs a number")
  return number
}

function singleEntryControl(listName, value, label) {
  if (isShortList(listName)) {
    const select = entrySelect(listName, value, label)
    return { control: select, read: () => Number(select.value) }
  }
  const picker = itemPicker(() => listEntries(listName), null, label)
  const current = listEntries(listName).find(entry => entry.id === value)
  if (current) picker.show(current)
  else if (!isEmptyValue(value)) picker.show({ id: value, label: "Unknown (" + value + ")", type: "" })
  return {
    control: picker.element,
    read: () => {
      const chosen = picker.selected()
      if (!chosen) throw new Error("Pick a value for " + label)
      return chosen.id
    },
  }
}

function entryListEditor(listName, values, label, hint) {
  const ids = Array.isArray(values) ? [...values] : []
  const chips = h("div", { class: "chips" })
  const drawChips = () => fill(chips, ids.length === 0 ? h("span", { class: "muted" }, "None") : ids.map((id, index) => h("span", { class: "chip" },
    chipLabel(listName, id),
    h("button", { type: "button", "aria-label": "Remove " + entryName(listName, id), onclick: () => { ids.splice(index, 1); drawChips() } }, "×"))))
  const add = entry => {
    if (ids.includes(entry.id)) return toast(entry.label + " is already in the list", true)
    ids.push(entry.id)
    drawChips()
  }
  const picker = itemPicker(() => listEntries(listName).filter(entry => !ids.includes(entry.id)), add, label)
  const clearAll = h("button", { class: "button small", type: "button", onclick: () => { ids.length = 0; drawChips() } }, "Clear all")
  drawChips()
  return {
    element: h("div", { class: "field" }, h("span", null, label), hint,
      h("div", { class: "row" }, h("div", { class: "grow" }, picker.element), clearAll),
      h("div", { class: "entry-chips" }, chips)),
    read: () => [...ids],
  }
}

function slotsEditor(slotList, values, label, hint) {
  const slots = catalog[slotList] || []
  const count = Math.max(slots.length, Array.isArray(values) ? values.length : 0)
  const inputs = Array.from({ length: count }, (_, index) => {
    const slotLabel = slots[index] ? slots[index].name : "#" + (index + 1)
    return { label: slotLabel, input: numberInput(Array.isArray(values) ? values[index] : undefined, label + " " + slotLabel) }
  })
  return {
    element: h("div", { class: "field" }, h("span", null, label), hint,
      h("div", { class: "slots" }, inputs.map(slot => h("label", { class: "slot" }, h("span", null, slot.label), slot.input)))),
    read: () => inputs.map(slot => readNumber(slot.input, label + " " + slot.label, false)),
  }
}

function mapValueControl(schema, field, value, label) {
  if (field.pick) return singleEntryControl(field.pick, value, label)
  if (schema.kind === "list" && field.valueLabels) {
    const inputs = field.valueLabels.map((valueLabel, index) => ({ valueLabel, input: numberInput(Array.isArray(value) ? value[index] : undefined, valueLabel) }))
    return {
      control: h("div", { class: "row" }, inputs.map(item => h("label", { class: "slot" }, h("span", null, item.valueLabel), item.input))),
      read: () => inputs.map(item => readNumber(item.input, label + " " + item.valueLabel, false)),
    }
  }
  const input = numberInput(value, label)
  return { control: input, read: () => readNumber(input, label, false) }
}

function mapEditor(schema, field, value, label, hint) {
  const rows = []
  const container = h("div", { class: "map-rows" })
  const keyLabel = field.keyPick ? "From" : "Key"
  const addRow = (key, rowValue) => {
    const keyControl = field.keyPick ? singleEntryControl(field.keyPick, key, keyLabel) : (() => {
      const input = numberInput(key, keyLabel)
      return { control: input, read: () => readNumber(input, keyLabel, false) }
    })()
    const valueControl = mapValueControl(schema.value, field, rowValue, label)
    const row = { keyControl, valueControl }
    row.element = h("div", { class: "map-row" },
      h("div", { class: "map-key" }, keyControl.control),
      h("span", { class: "map-arrow", "aria-hidden": "true" }, "→"),
      h("div", { class: "map-value" }, valueControl.control),
      h("button", { class: "button small danger", type: "button", onclick: () => { rows.splice(rows.indexOf(row), 1); row.element.remove() } }, "Remove"))
    rows.push(row)
    container.append(row.element)
  }
  const keyOrder = field.keyPick ? listEntries(field.keyPick).map(entry => entry.id) : []
  const rank = key => keyOrder.includes(key) ? keyOrder.indexOf(key) : keyOrder.length + key
  Object.entries(value || {})
    .map(([key, rowValue]) => [Number(key), rowValue])
    .sort(([left], [right]) => rank(right) - rank(left))
    .forEach(([key, rowValue]) => addRow(key, rowValue))
  const firstKey = () => {
    const entries = field.keyPick ? listEntries(field.keyPick) : []
    const used = rows.map(row => { try { return row.keyControl.read() } catch { return null } })
    const free = entries.find(entry => !used.includes(entry.id))
    return free ? free.id : entries.length ? entries[0].id : null
  }
  return {
    element: h("div", { class: "field" }, h("span", null, label), hint, container,
      h("div", null, h("button", { class: "button small", type: "button", onclick: () => addRow(firstKey(), field.pick ? firstKey() : undefined) }, "Add"))),
    read: () => {
      const result = {}
      for (const row of rows) {
        const key = row.keyControl.read()
        if (Object.hasOwn(result, String(key))) {
          throw new Error(label + " lists " + (field.keyPick ? entryName(field.keyPick, key) : key) + " twice")
        }
        result[String(key)] = row.valueControl.read()
      }
      return result
    },
  }
}

function valueEditor(schema, value, label, hint, field) {
  const meta = field || {}
  const wrap = control => h("label", { class: "field" }, h("span", null, label), control, hint)
  if (meta.choices && (schema.kind === "number" || schema.kind === "text")) {
    const select = choiceSelect(meta.choices, value, label)
    return { element: wrap(select), read: () => schema.kind === "number" ? Number(select.value) : select.value }
  }
  if (meta.pick && schema.kind === "number") {
    const entry = singleEntryControl(meta.pick, value, label)
    return { element: wrap(entry.control), read: entry.read }
  }
  if (meta.pick && schema.kind === "list") return entryListEditor(meta.pick, value, label, hint)
  if (meta.slots && schema.kind === "list") return slotsEditor(meta.slots, value, label, hint)
  if (schema.kind === "map") return mapEditor(schema, meta, value, label, hint)
  switch (schema.kind) {
    case "bool": {
      const input = h("input", { type: "checkbox", checked: Boolean(value) })
      return { element: h("div", { class: "field" }, h("label", { class: "check" }, input, label), hint), read: () => input.checked }
    }
    case "number": {
      const input = numberInput(value)
      return { element: wrap(input), read: () => readNumber(input, label, schema.nullable) }
    }
    case "text":
    case "time": {
      const input = h("input", { value: value || "", placeholder: schema.kind === "time" ? "2027-01-01T00:00:00Z" : null })
      return { element: wrap(input), read: () => (input.value === "" && schema.nullable) || (schema.kind === "time" && input.value.trim() === "") ? null : input.value }
    }
    case "longtext": {
      const input = h("textarea", { rows: "4", value: value || "" })
      return { element: wrap(input), read: () => input.value }
    }
    case "list": {
      const input = h("input", { value: Array.isArray(value) ? value.join(", ") : "", placeholder: "Comma separated" })
      return {
        element: wrap(input),
        read: () => {
          const parts = input.value.split(",").map(part => part.trim()).filter(part => part !== "")
          if (schema.item !== "number") return parts
          return parts.map(part => {
            const number = Number(part)
            if (!Number.isFinite(number)) throw new Error(label + " has a value that is not a number")
            return number
          })
        },
      }
    }
    case "json": {
      const input = h("textarea", { class: "mono", rows: "5", value: JSON.stringify(value === undefined ? null : value, null, 2) })
      return {
        element: wrap(input),
        read: () => {
          try { return JSON.parse(input.value) } catch { throw new Error(label + " is not valid JSON") }
        },
      }
    }
    case "object": {
      const editor = objectEditor(schema.fields, value || {})
      return { element: h("div", { class: "form-object" }, h("div", { class: "form-object-head" }, label), hint, editor.element), read: editor.read }
    }
    case "objects": {
      const compact = schema.fields.length <= COMPACT_FIELD_LIMIT && schema.fields.every(item => ["number", "text", "bool"].includes(item.schema.kind))
      const items = []
      const container = h("div")
      const addItem = itemValue => {
        const editor = objectEditor(schema.fields, itemValue)
        const entry = { editor }
        entry.element = h("div", { class: compact ? "form-object compact" : "form-object" },
          h("div", { class: "form-object-head" }, h("span", null, ""), h("button", {
            class: "button small danger", type: "button",
            onclick: () => { items.splice(items.indexOf(entry), 1); entry.element.remove(); renumber() },
          }, "Remove")),
          editor.element)
        items.push(entry)
        container.append(entry.element)
        renumber()
      }
      const renumber = () => items.forEach((entry, index) => { entry.element.firstChild.firstChild.textContent = "#" + (index + 1) })
      ;(Array.isArray(value) ? value : []).forEach(addItem)
      return {
        element: h("div", { class: "field" },
          h("div", { class: "form-group-title" }, label),
          hint,
          container,
          h("div", null, h("button", { class: "button small", type: "button", onclick: () => addItem(structuredClone(schema.template || {})) }, "Add"))),
        read: () => items.map(entry => entry.editor.read()),
      }
    }
    default:
      return { element: h("div"), read: () => value }
  }
}

async function squadsView(page, argument) {
  if (argument) return squadDetailView(page, argument)
  const squads = await api("GET", "/squads")
  const body = squads.length === 0 ? h("div", { class: "empty" }, "No squads yet") : h("table", null,
    h("thead", null, h("tr", null, h("th", null, "Squad"), h("th", null, "ID"), h("th", null, "Leader"), h("th", null, "Members"), h("th", { class: "hide-small" }, "Created"))),
    h("tbody", null, squads.map(squad => h("tr", { class: "link", onclick: () => { location.hash = "#squads/" + squad.guildID } },
      h("td", null, squad.name),
      h("td", { class: "mono" }, squad.shortId),
      h("td", null, trainerLink(squad.leader)),
      h("td", { class: "num" }, squad.members),
      h("td", { class: "hide-small" }, formatTime(squad.buildTime))))))
  fill(page, pageHead("Squads"), card(null, h("div", { class: "card-body flush" }, body)))
}

async function squadDetailView(page, guildID) {
  let detail = await api("GET", "/squads/" + encodeURIComponent(guildID))
  const remove = async member => {
    const name = member.trainer.name || String(member.trainer.uid)
    if (!await modal({ title: "Remove " + name + "?", text: "They leave " + detail.squad.name + ".", confirm: "Remove", danger: true })) return
    detail = await api("POST", "/squads/" + encodeURIComponent(guildID) + "/members/" + member.trainer.uid + "/remove")
    toast("Removed")
    draw()
  }
  const draw = () => {
    const members = h("table", null,
      h("thead", null, h("tr", null, h("th", null, "Trainer"), h("th", null, "Role"), h("th", { class: "hide-small" }, "Joined"), h("th", null))),
      h("tbody", null, detail.members.map(member => h("tr", null,
        h("td", null, trainerLink(member.trainer)),
        h("td", null, member.isLeader ? h("span", { class: "badge green" }, "Leader") : "Member"),
        h("td", { class: "hide-small" }, formatTime(member.joinTime)),
        h("td", { class: "end" }, member.isLeader ? null : h("button", { class: "button small", type: "button", onclick: () => remove(member).catch(fail) }, "Remove"))))))
    fill(page,
      h("div", { class: "page-head" }, h("div", null, h("a", { class: "back", href: "#squads" }, "Squads"), h("h1", null, detail.squad.name)), h("div", { class: "actions" }, h("span", { class: "badge" }, detail.squad.shortId))),
      detail.notice ? card("Notice", h("div", { class: "card-body" }, h("p", { class: "wrap" }, detail.notice))) : null,
      card("Members", h("div", { class: "card-body flush" }, members), h("span", { class: "badge" }, detail.members.length)))
  }
  draw()
}

async function reportsView(page) {
  const rows = h("tbody")
  const more = h("button", { class: "button", type: "button" }, "Load more")
  let before = null
  const load = async () => {
    const result = await api("GET", "/reports" + (before ? "?before=" + before : ""))
    rows.append(...result.reports.map(report => h("tr", null,
      h("td", { class: "num nowrap" }, formatTime(report.accusationTime)),
      h("td", null, trainerLink(report.accused)),
      h("td", null, report.category, h("div", { class: "muted" }, report.scene)),
      h("td", null, report.reasons.length ? h("div", { class: "chips" }, report.reasons.map(reason => h("span", { class: "chip" }, reason))) : h("span", { class: "muted" }, "None"),
        report.creditPunished ? h("div", null, h("span", { class: "badge amber" }, "Credit taken")) : null),
      h("td", { class: "muted wrap hide-small" }, [report.desc, report.content].filter(Boolean).join(" ")),
      h("td", { class: "hide-small" }, trainerLink(report.reporter)))))
    before = result.next
    more.hidden = !result.next
    if (rows.children.length === 0) rows.append(h("tr", null, h("td", { colspan: "6", class: "empty" }, "No reports")))
  }
  more.addEventListener("click", () => load().catch(fail))
  fill(page,
    pageHead("Reports"),
    card(null, h("div", { class: "card-body flush" }, h("table", null,
      h("thead", null, h("tr", null, h("th", null, "Time"), h("th", null, "Reported"), h("th", null, "Type"), h("th", null, "Reasons"), h("th", { class: "hide-small" }, "Note"), h("th", { class: "hide-small" }, "By"))),
      rows))),
    h("div", { class: "more" }, more))
  await load()
}

const LOOKUP_GROUPS = [
  ["pokemon", "Pokemon"],
  ["items", "Items"],
  ["coins", "Coins"],
  ["licenses", "Unite licenses"],
  ["holowear", "Holowear"],
  ["outfits", "Outfits"],
  ["icons", "Icons"],
  ["frames", "Frames"],
  ["backdrops", "Backdrops"],
  ["emblems", "Emblems"],
  ["maps", "Maps"],
]
const LOOKUP_LIMIT = 100

async function lookupView(page) {
  await loadCatalog()
  const all = LOOKUP_GROUPS.flatMap(([list]) => catalog[list].map(entry => ({ ...entry, label: entry.name })))
  const group = h("select", { "aria-label": "Group", class: "kind" }, h("option", { value: "" }, "Everything"), LOOKUP_GROUPS.map(([list, label]) => h("option", { value: list }, label)))
  const input = h("input", { type: "search", placeholder: "Search by name, type or id", "aria-label": "Search" })
  const results = h("div", { class: "card-body flush" })
  const count = h("span", { class: "badge" })
  const draw = () => {
    const query = input.value.trim().toLowerCase()
    const pool = group.value ? catalog[group.value].map(entry => ({ ...entry, label: entry.name })) : all
    const found = pool.filter(entry => matchesQuery(entry, query))
    count.textContent = formatNumber(found.length)
    if (found.length === 0) return fill(results, h("div", { class: "empty" }, "Nothing found"))
    fill(results, h("table", null,
      h("thead", null, h("tr", null, h("th", null, "Name"), h("th", null, "Type"), h("th", null, "ID"), h("th", null, ""))),
      h("tbody", null, found.slice(0, LOOKUP_LIMIT).map(entry => h("tr", null,
        h("td", null, entry.name),
        h("td", { class: "muted" }, entry.type),
        h("td", { class: "mono" }, entry.id),
        h("td", { class: "end" }, h("button", {
          class: "button small", type: "button",
          onclick: () => navigator.clipboard.writeText(String(entry.id)).then(() => toast("Copied " + entry.id)).catch(fail),
        }, "Copy")))))),
    found.length > LOOKUP_LIMIT ? h("div", { class: "empty" }, "Showing the first " + LOOKUP_LIMIT + ". Search to narrow it down") : null)
  }
  let timer = 0
  input.addEventListener("input", () => { clearTimeout(timer); timer = setTimeout(draw, 150) })
  group.addEventListener("change", draw)
  fill(page,
    pageHead("Lookup"),
    h("p", { class: "section-intro" }, "Find any Pokemon, item, cosmetic or map by name or id."),
    h("div", { class: "row spaced" }, group, h("div", { class: "search grow" }, svg(SEARCH_ICON), input)),
    card("Results", results, count))
  input.focus()
  draw()
}

function cfgTimeFromInput(value) {
  return value ? Number(value.replace(/[-T:]/g, "").padEnd(14, "0")) : 0
}

function formatCfgTime(cfgTime) {
  if (!cfgTime) return "Never"
  const text = String(cfgTime)
  return text.slice(0, 4) + "-" + text.slice(4, 6) + "-" + text.slice(6, 8) + " " + text.slice(8, 10) + ":" + text.slice(10, 12) + " UTC"
}

async function activationCodesPanel() {
  const codes = await api("GET", "/activation-codes")
  const code = h("input", { maxlength: "32", placeholder: "Code", required: true, "aria-label": "Code" })
  const validUntil = h("input", { type: "datetime-local", "aria-label": "Valid until (UTC)" })
  const maxUses = h("input", { type: "number", min: "0", value: "0", "aria-label": "Max uses" })
  const create = h("form", {
    class: "row bottom",
    onsubmit: async event => {
      event.preventDefault()
      try {
        const added = await api("POST", "/activation-codes", {
          activationCode: code.value,
          validEndTime: cfgTimeFromInput(validUntil.value),
          maxUseCnt: Number(maxUses.value || 0),
        })
        codes.push(added)
        codes.sort((left, right) => left.activationCode.localeCompare(right.activationCode))
        code.value = ""
        validUntil.value = ""
        maxUses.value = "0"
        drawTable()
        toast("Code added")
      } catch (error) {
        fail(error)
      }
    },
  },
  h("label", { class: "field grow" }, h("span", null, "Code"), code),
  h("label", { class: "field" }, h("span", null, "Valid until (UTC)"), validUntil),
  h("label", { class: "field" }, h("span", null, "Max uses"), maxUses),
  h("button", { class: "button primary", type: "submit" }, "Add"))

  const tableHolder = h("div", { class: "card-body flush" })
  const count = h("span", { class: "badge" })
  const drawTable = () => (count.textContent = codes.length, fill(tableHolder, codes.length === 0 ? h("div", { class: "empty" }, "No codes yet") : h("table", null,
    h("thead", null, h("tr", null, h("th", null, "Code"), h("th", null, "Valid until"), h("th", null, "Uses"), h("th", null, ""))),
    h("tbody", null, codes.map(entry => h("tr", null,
      h("td", { class: "mono" }, entry.activationCode),
      h("td", null, formatCfgTime(entry.validEndTime)),
      h("td", { class: "num" }, entry.useCnt + " of " + (entry.maxUseCnt === 0 ? "unlimited" : entry.maxUseCnt)),
      h("td", { class: "end" }, h("button", {
        class: "button small danger", type: "button",
        onclick: async () => {
          if (!await modal({ title: "Remove " + entry.activationCode + "?", text: "Players can no longer use it. Its past uses are kept.", confirm: "Remove", danger: true })) return
          try {
            await api("DELETE", "/activation-codes/" + encodeURIComponent(entry.activationCode))
            codes.splice(codes.indexOf(entry), 1)
            drawTable()
            toast("Removed")
          } catch (error) {
            fail(error)
          }
        },
      }, "Remove"))))))))
  drawTable()
  return h("div", null,
    card("Activation Codes", h("div", { class: "card-body" },
      h("p", { class: "section-intro" }, "Closed beta codes for the client's code screen. Codes match exactly and grant nothing. Max uses 0 means unlimited."),
      create)),
    card("Codes", tableHolder, count))
}

function rewardLabel(attachment) {
  const kind = KINDS.find(item => item.key === attachment.kind)
  const name = kind ? catalogName(kind.list, attachment.id, String(attachment.id)) : String(attachment.id)
  return name + (attachment.count > 1 ? " x" + formatNumber(attachment.count) : "")
}

async function giftCodeUsesView(entry) {
  const uses = await api("GET", "/gift-codes/" + encodeURIComponent(entry.giftCode) + "/uses")
  const body = uses.length === 0 ? h("div", { class: "empty" }, "Nobody used this code yet") : h("table", null,
    h("thead", null, h("tr", null, h("th", null, "Trainer"), h("th", null, "Player ID"), h("th", null, "Used"))),
    h("tbody", null, uses.map(use => h("tr", null,
      h("td", null, use.trainer.name || h("span", { class: "muted" }, "No trainer")),
      h("td", { class: "mono" }, String(use.trainer.uid)),
      h("td", null, formatTime(use.useTime))))))
  await modal({ title: "Uses of " + entry.giftCode, body: h("div", { class: "modal-list" }, body), confirm: "Close" })
}

async function giftCodesView(page) {
  await loadCatalog()
  const codes = await api("GET", "/gift-codes")
  const code = h("input", { maxlength: "32", placeholder: "Code", required: true, "aria-label": "Code" })
  const validFrom = h("input", { type: "datetime-local", "aria-label": "Valid from (UTC)" })
  const validUntil = h("input", { type: "datetime-local", required: true, "aria-label": "Valid until (UTC)" })
  const maxUses = h("input", { type: "number", min: "0", value: "0", "aria-label": "Max uses" })
  const rewards = rewardPicker("Rewards")
  const addButton = h("button", { class: "button primary", type: "submit" }, "Add")
  const create = h("form", {
    onsubmit: async event => {
      event.preventDefault()
      addButton.disabled = true
      try {
        const added = await api("POST", "/gift-codes", {
          giftCode: code.value,
          validStartTime: cfgTimeFromInput(validFrom.value),
          validEndTime: cfgTimeFromInput(validUntil.value),
          maxUseCnt: Number(maxUses.value || 0),
          attachments: rewards.list(),
        })
        codes.unshift(added)
        code.value = ""
        validFrom.value = ""
        validUntil.value = ""
        maxUses.value = "0"
        rewards.clear()
        drawTable()
        toast("Code added")
      } catch (error) {
        fail(error)
      } finally {
        addButton.disabled = false
      }
    },
  },
  h("div", { class: "card-body" },
    h("div", { class: "row bottom gift-code-head" },
      h("label", { class: "field grow" }, h("span", null, "Code"), code),
      h("label", { class: "field" }, h("span", null, "Valid from (UTC)"), validFrom),
      h("label", { class: "field" }, h("span", null, "Valid until (UTC)"), validUntil),
      h("label", { class: "field" }, h("span", null, "Max uses"), maxUses)),
    rewards.element),
  h("div", { class: "form-footer" }, addButton))

  const setEnabled = async (entry, isEnabled) => {
    try {
      await api("POST", "/gift-codes/" + encodeURIComponent(entry.giftCode) + (isEnabled ? "/enable" : "/disable"))
      entry.isEnabled = isEnabled
      drawTable()
      toast(isEnabled ? "Code enabled" : "Code disabled")
    } catch (error) {
      fail(error)
    }
  }

  const remove = async entry => {
    if (!await modal({ title: "Delete " + entry.giftCode + "?", text: "Players can no longer use it, and its use list is deleted. Mail already sent stays.", confirm: "Delete", danger: true })) return
    try {
      await api("DELETE", "/gift-codes/" + encodeURIComponent(entry.giftCode))
      codes.splice(codes.indexOf(entry), 1)
      drawTable()
      toast("Code deleted")
    } catch (error) {
      fail(error)
    }
  }

  const tableHolder = h("div", { class: "card-body flush" })
  const drawTable = () => fill(tableHolder, codes.length === 0 ? h("div", { class: "empty" }, "No gift codes yet") : h("table", null,
    h("thead", null, h("tr", null, h("th", null, "Code"), h("th", null, "Rewards"), h("th", { class: "hide-small" }, "Valid"), h("th", null, "Uses"), h("th", null, "Status"), h("th", null, ""))),
    h("tbody", null, codes.map(entry => h("tr", null,
      h("td", { class: "mono" }, entry.giftCode),
      h("td", null, entry.attachments.map(rewardLabel).join(", ")),
      h("td", { class: "hide-small" }, (entry.validStartTime ? formatCfgTime(entry.validStartTime) : "Now") + " to " + formatCfgTime(entry.validEndTime)),
      h("td", { class: "num" }, entry.useCnt + " of " + (entry.maxUseCnt === 0 ? "unlimited" : entry.maxUseCnt)),
      h("td", null, h("span", { class: entry.isEnabled ? "badge green" : "badge" }, entry.isEnabled ? "On" : "Off")),
      h("td", { class: "end" },
        h("div", { class: "actions" },
          h("button", { class: "button small", type: "button", onclick: () => giftCodeUsesView(entry).catch(fail) }, "Uses"),
          h("button", { class: "button small", type: "button", onclick: () => setEnabled(entry, !entry.isEnabled) }, entry.isEnabled ? "Disable" : "Enable"),
          h("button", { class: "button small danger", type: "button", onclick: () => remove(entry) }, "Delete"))))))))
  drawTable()
  fill(page,
    pageHead("Gift Codes"),
    h("p", { class: "section-intro" }, "Codes players enter on the Gift Exchange page. Each trainer can use a code once, and the rewards arrive by mail. Codes are letters and digits, and case does not matter. Leave Valid from empty to start now. Max uses 0 means unlimited."),
    card("New code", create),
    card("Codes", tableHolder, h("span", { class: "badge" }, codes.length)))
}

async function tokensView(page) {
  const tokens = await api("GET", "/tokens")
  const secretBox = h("div")
  const name = h("input", { maxlength: "40", placeholder: "Name", required: true, "aria-label": "Token name" })
  const create = h("form", {
    class: "row",
    onsubmit: async event => {
      event.preventDefault()
      try {
        const issued = await api("POST", "/tokens", { name: name.value })
        const copy = h("button", { class: "button small", type: "button", onclick: () => navigator.clipboard.writeText(issued.token).then(() => toast("Copied")).catch(fail) }, "Copy")
        fill(secretBox, h("div", { class: "secret" }, h("code", null, issued.token), copy))
        name.value = ""
        tokens.push(issued.info)
        drawTable()
        toast("Token created. Copy it now, it is shown once")
      } catch (error) {
        fail(error)
      }
    },
  }, h("div", { class: "grow" }, name), h("button", { class: "button primary", type: "submit" }, "Create"))

  const tableHolder = h("div", { class: "card-body flush" })
  const drawTable = () => fill(tableHolder, h("table", null,
    h("thead", null, h("tr", null, h("th", null, "Name"), h("th", { class: "hide-small" }, "Created"), h("th", null, "Last used"), h("th", null, ""))),
    h("tbody", null, tokens.map(token => h("tr", null,
      h("td", null, token.tokenName, token.tokenName === adminName ? h("span", { class: "muted" }, " (you)") : null),
      h("td", { class: "hide-small" }, formatTime(token.createdTime)),
      h("td", null, formatTime(token.lastUsedTime)),
      h("td", { class: "end" }, h("button", {
        class: "button small danger", type: "button",
        onclick: async () => {
          if (!await modal({ title: "Revoke " + token.tokenName + "?", text: "Anyone using it is logged out.", confirm: "Revoke", danger: true })) return
          try {
            await api("DELETE", "/tokens/" + token.tokenId)
            tokens.splice(tokens.indexOf(token), 1)
            drawTable()
            toast("Revoked")
          } catch (error) {
            fail(error)
          }
        },
      }, "Revoke")))))))
  drawTable()
  fill(page, pageHead("Tokens"), secretBox, card("New token", h("div", { class: "card-body" }, create)), card("Active tokens", tableHolder))
}

async function logView(page) {
  const rows = h("tbody")
  const more = h("button", { class: "button", type: "button" }, "Load more")
  let before = null
  const load = async () => {
    const entries = await api("GET", "/audit" + (before ? "?before=" + before : ""))
    rows.append(...entries.map(entry => h("tr", null,
      h("td", { class: "num nowrap" }, formatTime(entry.auditTime)),
      h("td", null, entry.tokenName),
      h("td", null, h("span", { class: "badge" }, entry.action)),
      h("td", { class: "mono" }, entry.target),
      h("td", { class: "muted wrap hide-small" }, entry.detail))))
    if (entries.length > 0) before = entries[entries.length - 1].auditId
    more.hidden = entries.length < 50
    if (rows.children.length === 0) rows.append(h("tr", null, h("td", { colspan: "5", class: "empty" }, "Nothing yet")))
  }
  more.addEventListener("click", () => load().catch(fail))
  fill(page, 
    pageHead("Log"),
    card(null, h("div", { class: "card-body flush" }, h("table", null,
      h("thead", null, h("tr", null, h("th", null, "Time"), h("th", null, "Admin"), h("th", null, "Action"), h("th", null, "Target"), h("th", { class: "hide-small" }, "Detail"))),
      rows))),
    h("div", { class: "more" }, more))
  await load()
}

if (memoryToken) start().catch(() => showLogin())
else showLogin()
