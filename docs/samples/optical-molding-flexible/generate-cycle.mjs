import { createHash } from 'node:crypto'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = dirname(fileURLToPath(import.meta.url))
const outputArg = process.argv.indexOf('--out')
const outputDir = outputArg >= 0
  ? join(process.cwd(), process.argv[outputArg + 1])
  : join(root, 'generated')

const readJson = async name => JSON.parse(await readFile(join(root, name), 'utf8'))
const acquisition = await readJson('acquisition-profile.v1.json')
const recipeProfile = await readJson('recipe-profile.v1.json')
const recipe = await readJson('recipe-instance.example.json')
const phaseMapping = await readJson('phase-mapping.v1.json')

const fail = message => { throw new Error(message) }
const unique = values => new Set(values).size === values.length
const acquisitionCodes = acquisition.fields.map(field => field.code)
const recipeCodes = recipeProfile.parameters.map(parameter => parameter.code)
const resolvedCodes = Object.keys(recipe.resolvedParameters)

if (acquisition.samplePeriodMs !== 1000) fail('样例要求采样周期为 1000ms。')
if (acquisition.scanSemantics !== 'atomic-group') fail('传感器必须按同一扫描原子成组。')
if (acquisitionCodes.length !== 13 || !unique(acquisitionCodes)) fail('采集 Profile 必须包含 13 个唯一传感器代码。')
if (recipeCodes.length !== 31 || !unique(recipeCodes)) fail('配方 Profile 必须包含 31 个唯一参数代码。')
if (resolvedCodes.length !== recipeCodes.length || recipeCodes.some(code => !resolvedCodes.includes(code))) {
  fail('配方实例必须解析为 Profile 定义的 31 个完整参数。')
}
if (recipeProfile.changeReasonRequired !== false) fail('本样例不要求配方修改原因。')
if (phaseMapping.mappings.length !== 5) fail('阶段映射必须包含 5 个阶段。')

const cycleId = 'CYCLE-20260721-000001'
const edgeId = 'EDGE-001'
const machineId = 'GLASS-PRESS-01'
const startedAt = new Date('2026-07-21T08:00:00.000Z')
const commonContext = {
  product_series: recipe.productSeries,
  product_code: 'LENS-A-42',
  workpiece_id: 'LENS-A-20260721-000001',
  operation_code: 'optical-glass-molding',
  recipe_id: recipe.recipeId,
  recipe_version: String(recipe.version),
  recipe_template: recipe.profileRef,
  acquisition_profile: `${acquisition.profileId}@${acquisition.version}`
}

let seq = 0
const events = []

function uuidV7(date, number) {
  const timestamp = BigInt(date.getTime()).toString(16).padStart(12, '0')
  const counter = BigInt(number)
  const randomA = (counter & 0xfffn).toString(16).padStart(3, '0')
  const variantPart = (counter & 0xfffn).toString(16).padStart(3, '0')
  const tail = counter.toString(16).padStart(12, '0').slice(-12)
  return `${timestamp.slice(0, 8)}-${timestamp.slice(8, 12)}-7${randomA}-8${variantPart}-${tail}`
}

function event(eventType, occurredAt, data = {}, context = {}) {
  seq += 1
  return {
    eventId: uuidV7(occurredAt, seq),
    eventType,
    eventTypeVersion: 1,
    occurredAt: occurredAt.toISOString(),
    recordedAt: new Date(occurredAt.getTime() + 100).toISOString(),
    source: `edge/${edgeId}/PLC-01/optical-molding-adapter`,
    subject: { type: 'optical-molding-machine', id: machineId },
    context: { ...commonContext, ...context },
    data,
    correlationId: cycleId,
    seq
  }
}

function round(value, digits = 3) {
  return Number(value.toFixed(digits))
}

function phaseAt(second) {
  if (second < 90) return phaseMapping.mappings[0]
  if (second < 240) return phaseMapping.mappings[1]
  if (second < 360) return phaseMapping.mappings[2]
  if (second < 480) return phaseMapping.mappings[3]
  return phaseMapping.mappings[4]
}

function temperatures(second) {
  if (second < 90) return [25 + second * 5.5, 24 + second * 5.42]
  if (second < 240) return [520 + (second - 90) * 0.66, 512 + (second - 90) * 0.65]
  if (second < 360) return [620 + Math.sin(second / 7) * 1.8, 615 + Math.sin(second / 8) * 1.6]
  if (second < 480) return [620 - (second - 360) * 1.15, 615 - (second - 360) * 1.12]
  return [482 - (second - 480) * 3.35, 481 - (second - 480) * 3.32]
}

function valuesAt(second) {
  const phase = phaseAt(second).phaseCode
  const [upperTemperature, lowerTemperature] = temperatures(second)
  const heating = phase === 'preheat' || phase === 'soak'
  const holding = phase === 'press'
  const upperVoltage = heating ? 218 + Math.sin(second / 11) * 2 : holding ? 82 : 0
  const lowerVoltage = heating ? 217 + Math.sin(second / 13) * 2 : holding ? 78 : 0
  const upperCurrent = heating ? 7.8 + Math.sin(second / 9) * 0.4 : holding ? 2.1 : 0
  const lowerCurrent = heating ? 7.5 + Math.sin(second / 10) * 0.4 : holding ? 2.0 : 0
  const load = phase === 'press' ? 120 + Math.sin(second / 6) * 1.5 : phase === 'anneal' ? 35 : 0
  const servoPosition = phase === 'press' ? 12.5 : phase === 'cool' ? 18 + (second - 480) * 0.058 : 25
  const servoSpeed = second === 240 ? 1.2 : second === 480 ? 0.35 : 0

  return {
    'upper_mold.ir_temperature': round(upperTemperature),
    'upper_mold.current': round(upperCurrent),
    'upper_mold.voltage': round(upperVoltage),
    'lower_mold.ir_temperature': round(lowerTemperature),
    'lower_mold.current': round(lowerCurrent),
    'lower_mold.voltage': round(lowerVoltage),
    'press.load': round(load),
    'grating.position': round(servoPosition + 0.006),
    'servo.speed': round(servoSpeed),
    'chamber.vacuum': round(phase === 'cool' ? 65 : 12),
    'servo.position': round(servoPosition),
    'upper_mold.power': round(upperCurrent * upperVoltage),
    'lower_mold.power': round(lowerCurrent * lowerVoltage)
  }
}

const canonicalRecipe = JSON.stringify(
  Object.fromEntries(Object.entries(recipe.resolvedParameters).sort(([a], [b]) => a.localeCompare(b)))
)
const snapshotSha256 = createHash('sha256').update(canonicalRecipe).digest('hex')

events.push(event('cycle.started', startedAt, {
  expectedDurationMs: 600000,
  expectedSampleCount: 600,
  samplePeriodMs: acquisition.samplePeriodMs
}))
events.push(event('recipe.applied', startedAt, {
  recipeProfileRef: recipe.profileRef,
  recipeId: recipe.recipeId,
  recipeVersion: recipe.version,
  basedOn: recipe.basedOn,
  overrides: recipe.overrides,
  resolvedParameters: recipe.resolvedParameters,
  snapshotSha256
}))

let previousStep = null
for (let second = 0; second < 600; second += 1) {
  const occurredAt = new Date(startedAt.getTime() + second * 1000)
  const phase = phaseAt(second)
  const stepContext = {
    recipe_step: phase.sourceStep,
    recipe_step_name: `STEP-${phase.sourceStep}`
  }

  if (phase.sourceStep !== previousStep) {
    events.push(event('recipe.step_changed', occurredAt, {
      sourceStep: phase.sourceStep,
      sourceStepName: stepContext.recipe_step_name
    }, stepContext))
    previousStep = phase.sourceStep
  }

  const values = valuesAt(second)
  if (Object.keys(values).length !== 13 || acquisitionCodes.some(code => !(code in values))) {
    fail(`第 ${second} 秒没有形成完整的 13 值传感器组。`)
  }
  events.push(event('process.sample', occurredAt, {
    schemaRef: `${acquisition.profileId}@${acquisition.version}`,
    values
  }, stepContext))
}

events.push(event('cycle.completed', new Date(startedAt.getTime() + 600000), {
  durationMs: 600000,
  sampleCount: 600,
  completionStatus: 'completed'
}))

const samples = events.filter(item => item.eventType === 'process.sample')
if (samples.length !== 600) fail('完整周期必须有 600 条 process.sample。')
if (events.length !== 608) fail('完整事件链应包含 608 条事件。')

await mkdir(outputDir, { recursive: true })
const batches = []
for (let index = 0; index < events.length; index += 500) {
  const batch = { edgeId, events: events.slice(index, index + 500) }
  batches.push(batch)
  const batchNumber = String(batches.length).padStart(3, '0')
  await writeFile(join(outputDir, `event-batch-${batchNumber}.json`), `${JSON.stringify(batch, null, 2)}\n`)
}

await writeFile(join(outputDir, 'recipe-applied.example.json'), `${JSON.stringify(events[1], null, 2)}\n`)
await writeFile(join(outputDir, 'process-sample.example.json'), `${JSON.stringify(samples[240], null, 2)}\n`)
await writeFile(join(outputDir, 'summary.json'), `${JSON.stringify({
  cycleId,
  durationSeconds: 600,
  sensorCountPerSample: 13,
  sampleCount: samples.length,
  phaseCount: phaseMapping.mappings.length,
  eventCount: events.length,
  transportBatchSizes: batches.map(batch => batch.events.length),
  recipeParameterCount: recipeCodes.length,
  recipeSnapshotSha256: snapshotSha256
}, null, 2)}\n`)

console.log(`Generated ${events.length} events (${samples.length} samples) in ${outputDir}`)
console.log(`Transport batches: ${batches.map(batch => batch.events.length).join(' + ')}`)
