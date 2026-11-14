// A real player on the test server: Forge handshake, control-center forms over
// the mod's channel, HUD/fog packets, digging a tank wall, the water gauge,
// a valve, TNT, and the control-center item. Prints PASS/FAIL per check.
const mc = require('minecraft-protocol')
const mineflayer = require('mineflayer')
const { forgeHandshake } = require('minecraft-protocol-forge')
const nbt = require('prismarine-nbt')
const { Vec3 } = require('vec3')
const fs = require('fs')

const SERVER = process.argv[2]
const FIFO = SERVER + '/in'
const NAME = 'RwTester'
const sleep = ms => new Promise(r => setTimeout(r, ms))
const cons = c => fs.writeFileSync(FIFO, c + '\n')
const results = []
function check (name, ok, detail) {
  results.push([name, ok])
  console.log((ok ? 'PASS ' : 'FAIL ') + name + (detail ? ' - ' + detail : ''))
}
function consoleSince (mark) {
  return fs.readFileSync(SERVER + '/console.log', 'utf8').slice(mark)
}
const consoleMark = () => fs.statSync(SERVER + '/console.log').size

// ---- mod channel
const forms = []
const formWaiters = []
const huds = []
const fogs = []
function fromNbt (v) {
  if (v && typeof v === 'object' && !Array.isArray(v)) {
    const mark = Object.keys(v).find(k => k.endsWith('list') && k.length > 4 && !/^[\x20-\x7e]/.test(k)) // "\u0000list" in modified UTF-8
    if (mark) {
      const n = v[mark]
      const out = []
      for (let i = 0; i < n; i++) out.push(fromNbt(v[String(i)]))
      return out
    }
    const o = {}
    for (const k of Object.keys(v)) o[k] = fromNbt(v[k])
    return o
  }
  return v
}
function readString (buf, off) { // ByteBufUtils.readUTF8String: varint length + utf8
  let len = 0; let shift = 0; let b
  do { b = buf[off++]; len |= (b & 0x7f) << shift; shift += 7 } while (b & 0x80)
  return [buf.slice(off, off + len).toString('utf8'), off + len]
}
function onPayload (p) {
  if (p.channel !== 'realisticworld') return
  const d = p.data
  if (d[0] === 0) {
    const parsed = nbt.parseUncompressed(d.slice(1), 'big')
    const form = fromNbt(nbt.simplify(parsed))
    forms.push(form)
    const w = formWaiters.shift()
    if (w) w(form)
  } else if (d[0] === 2) {
    const n = d[1]; let off = 2; const lines = []
    for (let i = 0; i < n; i++) { let s; [s, off] = readString(d, off); lines.push(s) }
    huds.push(lines)
  } else if (d[0] === 3) {
    fogs.push(readString(d, 1)[0])
  }
}
function nextForm (ms = 5000) {
  return new Promise((resolve, reject) => {
    const t = setTimeout(() => reject(new Error('no form within ' + ms + ' ms')), ms)
    formWaiters.push(f => { clearTimeout(t); resolve(f) })
  })
}
const buttons = f => f.elements.filter(e => e.type === 'button').map(e => e.text)
let client
function reply (form, selection, values = {}, canceled = false) {
  const v = {}
  for (const [k, val] of Object.entries(values)) {
    v[k] = typeof val === 'boolean' ? { type: 'byte', value: val ? 1 : 0 }
      : Number.isInteger(val) ? { type: 'int', value: val } : { type: 'double', value: val }
  }
  const root = {
    type: 'compound',
    name: '',
    value: {
      id: { type: 'int', value: form.id },
      canceled: { type: 'byte', value: canceled ? 1 : 0 },
      selection: { type: 'int', value: selection },
      values: { type: 'compound', value: v }
    }
  }
  client.write('custom_payload', { channel: 'realisticworld', data: Buffer.concat([Buffer.from([1]), nbt.writeUncompressed(root, 'big')]) })
}
async function pick (form, label) {
  const i = buttons(form).findIndex(b => b.startsWith(label))
  if (i < 0) throw new Error('no button "' + label + '" in ' + JSON.stringify(buttons(form)))
  const next = nextForm()
  reply(form, i)
  return next
}

// ---- run
async function main () {
  client = mc.createClient({ host: 'localhost', port: 25565, username: NAME, version: '1.12.2' })
  forgeHandshake(client, {
    forgeMods: [
      { modid: 'minecraft', version: '1.12.2' }, { modid: 'mcp', version: '9.42' },
      { modid: 'FML', version: '8.0.99.99' }, { modid: 'forge', version: '14.23.5.2860' },
      { modid: 'realisticworld', version: '0.3.1' }
    ]
  })
  client.on('custom_payload', onPayload)
  const bot = mineflayer.createBot({ client, username: NAME, version: '1.12.2' })
  const chat = []
  bot.on('message', m => chat.push(m.toString()))
  bot.on('kicked', r => console.log('KICKED', r))
  bot.on('error', e => console.log('ERROR', e.message))
  await new Promise((resolve, reject) => { bot.once('spawn', resolve); setTimeout(() => reject(new Error('no spawn')), 30000) })
  check('joined the Forge server with the mod', true)
  cons('op ' + NAME); cons('gamemode 1 ' + NAME)
  cons('fill -140 120 310 -134 125 316 stone'); cons('fill -139 121 311 -135 125 315 air'); cons('fill -139 121 311 -135 124 315 water')
  cons('fill -133 120 311 -131 120 315 glass')
  cons('tp ' + NAME + ' -131.5 121 313.5')
  await sleep(2500)

  // control center through the command
  let f = nextForm(); bot.chat('/rw menu'); f = await f
  check('/rw menu sends the control center form', f.title.includes('REALISTIC WORLD') && buttons(f).length >= 8, buttons(f).join(' | '))
  f = await pick(f, 'Dashboard')
  check('dashboard page', f.title === 'Dashboard' && f.body.includes('Sim tick'), f.body.split('\n').slice(0, 3).join(' / '))
  f = await pick(f, 'Back')
  f = await pick(f, 'Weather')
  check('weather page', f.title === 'Weather', f.body.split('\n').slice(0, 2).join(' / '))
  f = await pick(f, 'Force weather')
  const typeEl = f.elements.find(e => e.name === 'type')
  check('force weather form has the conditions', typeEl && typeEl.options.length === 8, typeEl && typeEl.options.join(', '))
  let wf = nextForm()
  reply(f, -1, { type: typeEl.options.findIndex(o => /fog/i.test(o)), minutes: 2.0 })
  f = await wf
  await sleep(4000)
  check('forced fog reaches the player (fog packet)', fogs.length > 0 && fogs.some(k => k !== ''), 'fog packets: ' + JSON.stringify(fogs.slice(-3)))
  f = await pick(f, 'Weather on my HUD')
  await sleep(2500)
  const wh = huds.filter(l => l.join(' ').match(/°C|C\b|rain|fog|clear/i))
  check('weather HUD lines arrive', huds.length > 0, JSON.stringify(huds[huds.length - 1]))
  f = await pick(f, 'Force weather')
  wf = nextForm()
  reply(f, -1, { type: typeEl.options.findIndex(o => /heavy/i.test(o)), minutes: 2.0 })
  f = await wf
  await sleep(3000)
  check('forced heavy rain makes it rain for the player', bot.isRaining === true, 'isRaining=' + bot.isRaining)
  f = await pick(f, 'Release forced weather')
  reply(f, -1, {}, true) // close
  await sleep(500)

  // performance page: modal with the HUD toggle
  f = nextForm(); bot.chat('/rw menu'); f = await f
  f = await pick(f, 'Performance')
  check('performance form', f.kind === 'MODAL' && f.elements.some(e => e.name === 'hud'), f.elements.map(e => e.name || e.type).join(','))
  huds.length = 0
  wf = nextForm(1500).catch(() => null)
  reply(f, -1, { profile: 1, adaptive: true, hud: true })
  await wf
  await sleep(2500)
  check('performance HUD lines arrive', huds.some(l => l.join(' ').includes('ms')), JSON.stringify(huds[huds.length - 1]))

  // dig a tank wall as a player
  let mark = consoleMark()
  chat.length = 0
  const wall = bot.blockAt(new Vec3(-134, 122, 313))
  check('tank wall is stone before digging', wall && wall.name === 'stone', wall && wall.name)
  await bot.dig(wall)
  await sleep(3000)
  let log = consoleSince(mark)
  check('breaking a wall next to water finds the tank (BreakEvent)', /found tank t\d+: 100 m.*1 openings/.test(log), (log.match(/found tank[^\n]*/) || ['none'])[0])
  cons('give ' + NAME + ' realisticworld:water_gauge'); cons('give ' + NAME + ' realisticworld:control_center')
  await sleep(1000)
  const modItems = bot.inventory.items().sort((a, b) => a.slot - b.slot)
  const gauge = modItems[0]
  const ccItem = modItems[1]
  check('mod items given (water gauge, control center)', modItems.length === 2, modItems.map(i => i.type + '@' + i.slot).join(','))
  cons('tp ' + NAME + ' -136.5 126 310.5') // on the tank rim: the leak pushed the player off the platform
  await sleep(1500)
  await bot.equip(gauge, 'hand')
  chat.length = 0
  await bot.activateBlock(bot.blockAt(new Vec3(-134, 123, 313)))
  await sleep(1500)
  check('water gauge on the wall reports the leaking tank', chat.some(l => /Leaking [\d.]+ L\/s through 1 opening/.test(l)), chat.join(' / '))

  // valve: closed valve, gauge, then open it by hand
  cons('fill -140 120 320 -134 125 326 stone'); cons('fill -139 121 321 -135 125 325 air')
  cons('setblock -134 121 323 realisticworld:valve 0'); cons('fill -139 121 321 -135 124 325 water')
  cons('fill -133 120 321 -131 120 325 glass'); cons('tp ' + NAME + ' -131.5 121 323.5')
  await sleep(2500)
  chat.length = 0
  await bot.activateBlock(bot.blockAt(new Vec3(-134, 121, 323)))
  await sleep(2500)
  check('gauge on a closed valve measures a sealed tank', chat.some(l => l.includes('Closed valves: 1')) && chat.some(l => l.includes('Sealed')), chat.join(' / '))
  await bot.unequip('hand')
  mark = consoleMark()
  await bot.activateBlock(bot.blockAt(new Vec3(-134, 121, 323)))
  await sleep(2500)
  const valve = bot.blockAt(new Vec3(-134, 121, 323))
  log = consoleSince(mark)
  check('right-clicking the valve opens it and the tank is rescanned', valve.metadata === 1 && /rescanned tank t\d+: 100 m.*1 openings/.test(log), 'meta=' + valve.metadata + ' ' + (log.match(/rescanned[^\n]*/) || ['none'])[0])
  await bot.equip(gauge, 'hand')
  chat.length = 0
  await bot.activateBlock(bot.blockAt(new Vec3(-134, 122, 323)))
  await sleep(1500)
  check('open valve drains the tank', chat.some(l => /Leaking [\d.]+ L\/s through 1 opening/.test(l)), chat.filter(l => /Tank|Leak/.test(l)).join(' / '))

  // TNT next to a full tank (entities tick while a player is online)
  cons('fill -140 120 330 -134 125 336 stone'); cons('fill -139 121 331 -135 125 335 air'); cons('fill -139 121 331 -135 124 335 water')
  await sleep(1000)
  mark = consoleMark()
  cons('summon tnt -132 122 333 {Fuse:1}')
  await sleep(4000)
  log = consoleSince(mark)
  check('an explosion next to a tank finds it with openings (ExplosionEvent)', /found tank t\d+: \d+ m.*[1-9]\d* openings/.test(log), (log.match(/found tank[^\n]*/) || ['none'])[0])
  const found = (log.match(/found tank t\d+/g) || []).length
  check('the explosion makes exactly one tank (no duplicates)', found === 1, found + ' "found tank" lines')

  // control center item
  const cc = ccItem
  await bot.equip(cc, 'hand')
  f = nextForm()
  bot.activateItem()
  try {
    f = await f
    check('using the Control Center item opens the menu', f.title.includes('REALISTIC WORLD'))
    reply(f, -1, {}, true)
  } catch (e) { check('using the Control Center item opens the menu', false, e.message) }

  await sleep(500)
  mark = consoleMark()
  bot.quit()
  await sleep(2000)
  log = consoleSince(mark)
  check('server still healthy after the session', !/Exception|ERROR/.test(log), log.split('\n').filter(l => /Exception|ERROR/.test(l)).join(' / '))
  const failed = results.filter(r => !r[1]).length
  console.log(`\n${results.length - failed}/${results.length} checks passed`)
  process.exit(failed ? 1 : 0)
}
main().catch(e => { console.log('FAIL script error: ' + e.stack); process.exit(2) })
