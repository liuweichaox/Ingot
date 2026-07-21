import { createHash } from 'node:crypto'
import { mkdir, readFile, readdir, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const root = dirname(fileURLToPath(import.meta.url))
const defaultDate = '2026-07-20'
const cycleSeconds = 600
const batchSize = 500

const readJson = async name => JSON.parse(await readFile(join(root, name), 'utf8'))
const round = (value, digits = 3) => Number(value.toFixed(digits))

function parseArgs(argv) {
  const value = (name, fallback) => {
    const index = argv.indexOf(name)
    return index >= 0 ? argv[index + 1] : fallback
  }
  const date = value('--date', defaultDate)
  const hours = Number(value('--hours', '8'))
  const outputDir = resolve(value('--out', join(root, 'generated', `factory-day-${date}`)))
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) throw new Error('--date 必须是 YYYY-MM-DD。')
  if (!Number.isFinite(hours) || hours <= 0 || (hours * 3600) % cycleSeconds !== 0) {
    throw new Error('--hours 必须大于 0，并且能被 10 分钟整除。')
  }
  return { date, hours, outputDir }
}

function uuidV7(date, number) {
  const timestamp = BigInt(date.getTime()).toString(16).padStart(12, '0')
  const counter = BigInt(number)
  const randomA = (counter & 0xfffn).toString(16).padStart(3, '0')
  const variantPart = ((counter >> 12n) & 0xfffn).toString(16).padStart(3, '0')
  const tail = counter.toString(16).padStart(12, '0').slice(-12)
  return `${timestamp.slice(0, 8)}-${timestamp.slice(8, 12)}-7${randomA}-8${variantPart}-${tail}`
}

function hashUnit(text) {
  const digest = createHash('sha256').update(text).digest()
  return digest.readUInt32BE(0) / 0xffffffff
}

function canonicalHash(value) {
  const sorted = Object.fromEntries(Object.entries(value).sort(([a], [b]) => a.localeCompare(b)))
  return createHash('sha256').update(JSON.stringify(sorted)).digest('hex')
}

const series = [
  {
    code: 'LENS-A', productCode: 'LENS-A-42', recipeVersion: 7,
    upperTemperature: 620, lowerTemperature: 615, pressure: 120, workPosition: 12.5,
    upperCore: 'CORE-UP-A023', lowerCore: 'CORE-LOW-A019'
  },
  {
    code: 'LENS-B', productCode: 'LENS-B-35', recipeVersion: 4,
    upperTemperature: 602, lowerTemperature: 597, pressure: 108, workPosition: 13.2,
    upperCore: 'CORE-UP-B011', lowerCore: 'CORE-LOW-B014'
  },
  {
    code: 'LENS-C', productCode: 'LENS-C-58', recipeVersion: 9,
    upperTemperature: 635, lowerTemperature: 630, pressure: 132, workPosition: 11.8,
    upperCore: 'CORE-UP-C031', lowerCore: 'CORE-LOW-C027'
  }
]

const machines = [
  { id: 'GLASS-PRESS-01', plc: 'PLC-01', temperatureBias: 0.7, loadBias: 0.4 },
  { id: 'GLASS-PRESS-02', plc: 'PLC-02', temperatureBias: -0.6, loadBias: -0.5 }
]

function phaseAt(second, phaseMapping) {
  if (second < 90) return phaseMapping.mappings[0]
  if (second < 240) return phaseMapping.mappings[1]
  if (second < 360) return phaseMapping.mappings[2]
  if (second < 480) return phaseMapping.mappings[3]
  return phaseMapping.mappings[4]
}

function temperaturesAt(second, profile, machine, variation, anomaly) {
  const targetUpper = profile.upperTemperature + machine.temperatureBias + variation
  const targetLower = profile.lowerTemperature + machine.temperatureBias * 0.8 + variation * 0.7
  let upper
  let lower
  if (second < 90) {
    upper = 25 + second * ((targetUpper - 100 - 25) / 90)
    lower = 24 + second * ((targetLower - 100 - 24) / 90)
  } else if (second < 240) {
    upper = targetUpper - 100 + (second - 90) * (100 / 150)
    lower = targetLower - 100 + (second - 90) * (100 / 150)
  } else if (second < 360) {
    const overshoot = anomaly === 'temperature-overshoot' && second < 285 ? 8 * (1 - (second - 240) / 45) : 0
    upper = targetUpper + Math.sin(second / 7) * 1.6 + overshoot
    lower = targetLower + Math.sin(second / 8) * 1.4 + overshoot * 0.75
  } else if (second < 480) {
    upper = targetUpper - (second - 360) * ((targetUpper - 430) / 120)
    lower = targetLower - (second - 360) * ((targetLower - 428) / 120)
  } else {
    upper = 430 - (second - 480) * (350 / 120)
    lower = 428 - (second - 480) * (348 / 120)
  }
  return [upper, lower]
}

function sensorValues(second, profile, machine, cycleKey, phaseMapping, anomaly) {
  const phase = phaseAt(second, phaseMapping).phaseCode
  const variation = (hashUnit(cycleKey) - 0.5) * 2.4
  const [upperTemperature, lowerTemperature] = temperaturesAt(
    second, profile, machine, variation, anomaly
  )
  const heating = phase === 'preheat' || phase === 'soak'
  const holding = phase === 'press'
  const upperVoltage = heating ? 218 + Math.sin(second / 11) * 2 : holding ? 82 : 0
  const lowerVoltage = heating ? 217 + Math.sin(second / 13) * 2 : holding ? 78 : 0
  const upperCurrent = heating ? 7.8 + Math.sin(second / 9) * 0.4 : holding ? 2.1 : 0
  const lowerCurrent = heating ? 7.5 + Math.sin(second / 10) * 0.4 : holding ? 2.0 : 0
  const loadDrift = anomaly === 'load-drift' && phase === 'press' ? (second - 240) * 0.025 : 0
  const load = phase === 'press'
    ? profile.pressure + machine.loadBias + Math.sin(second / 6) * 1.5 + loadDrift
    : phase === 'anneal' ? 35 : 0
  const servoPosition = phase === 'press'
    ? profile.workPosition
    : phase === 'cool' ? 18 + (second - 480) * 0.058 : 25
  const servoSpeed = second === 240 ? 1.2 : second === 480 ? 0.35 : 0
  const vacuum = anomaly === 'vacuum-drift' && phase === 'press' ? 18 : phase === 'cool' ? 65 : 12
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
    'chamber.vacuum': round(vacuum),
    'servo.position': round(servoPosition),
    'upper_mold.power': round(upperCurrent * upperVoltage),
    'lower_mold.power': round(lowerCurrent * lowerVoltage)
  }
}

function recipeFor(profile, baseRecipe, seriesOrdinal) {
  const derived = structuredClone(baseRecipe.resolvedParameters)
  derived['upper_mold.set_temperature'] = profile.upperTemperature
  derived['lower_mold.set_temperature'] = profile.lowerTemperature
  derived['work.set_pressure'] = profile.pressure + 8
  derived['hold.pressure'] = profile.pressure
  derived['position.work'] = profile.workPosition
  derived['upper_mold.core'] = profile.upperCore
  derived['lower_mold.core'] = profile.lowerCore
  derived['hold.upper_mold.temperature'] = profile.upperTemperature - 15
  derived['hold.lower_mold.temperature'] = profile.lowerTemperature - 15
  derived['work.upper_mold.temperature'] = profile.upperTemperature - 10
  derived['work.lower_mold.temperature'] = profile.lowerTemperature - 10
  derived['molding.temperature'] = profile.upperTemperature - 12
  const adjusted = seriesOrdinal % 8 === 4
  if (adjusted) {
    derived['upper_mold.set_temperature'] += 2
    derived['lower_mold.set_temperature'] += 2
  }
  const recipeId = `RCP-${profile.code}`
  return {
    recipeId,
    version: profile.recipeVersion + (adjusted ? 1 : 0),
    basedOn: { recipeId, version: profile.recipeVersion },
    overrides: adjusted
      ? { 'upper_mold.set_temperature': derived['upper_mold.set_temperature'], 'lower_mold.set_temperature': derived['lower_mold.set_temperature'] }
      : {},
    resolvedParameters: derived,
    adjusted
  }
}

function rawEvent({ eventType, occurredAt, data, context, correlationId, machine, localOrder }) {
  return { eventType, occurredAt, data, context, correlationId, machine, localOrder }
}

function makeCycle({ date, machine, machineIndex, cycleIndex, profile, profileIndex, start, acquisition, recipeProfile, baseRecipe, phaseMapping }) {
  const cycleNumber = cycleIndex + 1
  const cycleId = `CYCLE-${date.replaceAll('-', '')}-P${String(machineIndex + 1).padStart(2, '0')}-${String(cycleNumber).padStart(4, '0')}`
  const workpieceId = `${profile.code}-${date.replaceAll('-', '')}-P${machineIndex + 1}-${String(cycleNumber).padStart(4, '0')}`
  const seriesOrdinal = Math.floor(cycleIndex / series.length)
  const recipe = recipeFor(profile, baseRecipe, seriesOrdinal)
  const globalCycleIndex = machineIndex * 10_000 + cycleIndex
  const anomaly = globalCycleIndex % 23 === 7
    ? 'temperature-overshoot'
    : globalCycleIndex % 29 === 11 ? 'vacuum-drift'
      : globalCycleIndex % 31 === 13 ? 'load-drift' : null
  const context = {
    product_series: profile.code,
    product_code: profile.productCode,
    workpiece_id: workpieceId,
    operation_code: 'optical-glass-molding',
    recipe_id: recipe.recipeId,
    recipe_version: String(recipe.version),
    recipe_template: `${recipeProfile.profileId}@${recipeProfile.version}`,
    acquisition_profile: `${acquisition.profileId}@${acquisition.version}`
  }
  const events = []
  let localOrder = 0
  const add = (eventType, occurredAt, data = {}, extraContext = {}) => {
    events.push(rawEvent({
      eventType,
      occurredAt,
      data,
      context: { ...context, ...extraContext },
      correlationId: cycleId,
      machine,
      localOrder: localOrder++
    }))
  }
  add('cycle.started', start, { expectedDurationMs: 600000, expectedSampleCount: 600, samplePeriodMs: 1000 })
  add('recipe.applied', start, {
    recipeProfileRef: `${recipeProfile.profileId}@${recipeProfile.version}`,
    recipeId: recipe.recipeId,
    recipeVersion: recipe.version,
    basedOn: recipe.basedOn,
    overrides: recipe.overrides,
    resolvedParameters: recipe.resolvedParameters,
    snapshotSha256: canonicalHash(recipe.resolvedParameters)
  })
  let previousStep = null
  for (let second = 0; second < cycleSeconds; second += 1) {
    const occurredAt = new Date(start.getTime() + second * 1000)
    const phase = phaseAt(second, phaseMapping)
    const stepContext = { recipe_step: phase.sourceStep, recipe_step_name: `STEP-${phase.sourceStep}` }
    if (phase.sourceStep !== previousStep) {
      add('recipe.step_changed', occurredAt, {
        sourceStep: phase.sourceStep,
        sourceStepName: stepContext.recipe_step_name
      }, stepContext)
      previousStep = phase.sourceStep
    }
    add('process.sample', occurredAt, {
      schemaRef: `${acquisition.profileId}@${acquisition.version}`,
      values: sensorValues(second, profile, machine, cycleId, phaseMapping, anomaly)
    }, stepContext)
  }
  const completedAt = new Date(start.getTime() + cycleSeconds * 1000)
  add('cycle.completed', completedAt, { durationMs: 600000, sampleCount: 600, completionStatus: 'completed' })
  const visionFail = globalCycleIndex % 19 === 7 || anomaly === 'vacuum-drift'
  const manualFail = globalCycleIndex % 29 === 11
  const manualInconclusive = !manualFail && globalCycleIndex % 37 === 5
  return {
    events,
    cycle: {
      cycleId, workpieceId, machineId: machine.id, productSeries: profile.code,
      productCode: profile.productCode, startedAt: start.toISOString(), completedAt: completedAt.toISOString(),
      recipeId: recipe.recipeId, recipeVersion: recipe.version, recipeAdjusted: recipe.adjusted,
      anomaly, visionOutcome: visionFail ? 'FAIL' : 'PASS',
      manualOutcome: manualFail ? 'FAIL' : manualInconclusive ? 'INCONCLUSIVE' : 'PASS'
    },
    profile,
    profileIndex,
    visionFail,
    manualFail,
    manualInconclusive
  }
}

function bmpBuffer(cycle, fail) {
  const width = 128
  const height = 128
  const rowSize = Math.ceil((width * 3) / 4) * 4
  const pixelBytes = rowSize * height
  const buffer = Buffer.alloc(54 + pixelBytes)
  buffer.write('BM', 0, 2, 'ascii')
  buffer.writeUInt32LE(buffer.length, 2)
  buffer.writeUInt32LE(54, 10)
  buffer.writeUInt32LE(40, 14)
  buffer.writeInt32LE(width, 18)
  buffer.writeInt32LE(height, 22)
  buffer.writeUInt16LE(1, 26)
  buffer.writeUInt16LE(24, 28)
  buffer.writeUInt32LE(pixelBytes, 34)
  const tint = Math.floor(hashUnit(cycle.cycleId) * 18)
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const dx = x - width / 2
      const dy = y - height / 2
      const radius = Math.sqrt(dx * dx + dy * dy)
      const inLens = radius < 51
      let value = inLens ? Math.max(35, 205 - radius * 2.1 + tint) : 18
      if (inLens) value += Math.sin((x + y) / 7) * 5
      if (fail && x > 82 && x < 96 && y > 54 && y < 66) value = 250
      const offset = 54 + y * rowSize + x * 3
      buffer[offset] = Math.max(0, Math.min(255, value - 4))
      buffer[offset + 1] = Math.max(0, Math.min(255, value))
      buffer[offset + 2] = Math.max(0, Math.min(255, value + 3))
    }
  }
  return buffer
}

function inspectionRecords(cycleResult, date, recordBase) {
  const { cycle, profile, visionFail, manualFail, manualInconclusive } = cycleResult
  const completedAt = new Date(cycle.completedAt)
  const variation = (hashUnit(cycle.cycleId) - 0.5) * 0.04
  const visualTime = new Date(completedAt.getTime() + 20_000)
  const manualTime = new Date(completedAt.getTime() + 120_000)
  const originalImage = join('original-images', `${cycle.cycleId}-top.bmp`).replaceAll('\\', '/')
  const vision = {
    kind: 'vision',
    originalImage,
    record: {
      recordId: uuidV7(visualTime, recordBase),
      workpieceId: cycle.workpieceId,
      operationRunId: cycle.cycleId,
      definitionCode: 'optical.appearance.machine',
      definitionVersion: 1,
      measuredAt: visualTime.toISOString(),
      recordedAt: new Date(visualTime.getTime() + 1000).toISOString(),
      outcome: cycle.visionOutcome,
      submittedBy: 'OPERATOR-001',
      instrument: {
        instrumentId: cycle.machineId.endsWith('01') ? 'VISION-01' : 'VISION-02',
        model: 'AOI-SIM-01', calibrationRef: `CAL-VISION-${date.slice(0, 7)}`
      },
      measurements: [
        { characteristicCode: 'scratch_count', outcome: visionFail ? 'FAIL' : 'PASS', numericValue: visionFail ? 1 : 0, unit: '1', upperLimit: 0 },
        { characteristicCode: 'edge_chip_area', outcome: visionFail ? 'FAIL' : 'PASS', numericValue: visionFail ? 0.016 : round(0.003 + Math.abs(variation) / 10, 4), unit: 'mm2', upperLimit: 0.01 }
      ],
      attachments: [],
      notes: '模拟 AOI 原始图；导入时先上传 BMP 原图，再固化附件哈希与受控引用。'
    }
  }
  const pv = manualFail ? 0.31 : round(0.18 + variation, 3)
  const manualOutcome = manualFail ? 'FAIL' : manualInconclusive ? 'INCONCLUSIVE' : 'PASS'
  const manual = {
    kind: 'manual',
    record: {
      recordId: uuidV7(manualTime, recordBase + 1),
      workpieceId: cycle.workpieceId,
      operationRunId: cycle.cycleId,
      definitionCode: 'optical.final.manual',
      definitionVersion: 1,
      measuredAt: manualTime.toISOString(),
      recordedAt: new Date(manualTime.getTime() + 8000).toISOString(),
      outcome: manualOutcome,
      submittedBy: 'OPERATOR-001',
      instrument: {
        instrumentId: 'INTERFEROMETER-02', model: 'SIM-FIZEAU',
        calibrationRef: 'CAL-INT-2026-Q3', calibrationValidUntil: '2026-09-30T23:59:59Z'
      },
      measurements: [
        { characteristicCode: 'surface.pv', outcome: manualOutcome, numericValue: pv, unit: '1', upperLimit: 0.25 },
        { characteristicCode: 'visual.appearance', outcome: manualOutcome, textValue: manualFail ? '复核发现面形超差' : manualInconclusive ? '边缘区域需二次复核' : '无可见裂纹、缺口和异物' }
      ],
      attachments: [],
      notes: `模拟人工终检；产品系列 ${profile.code}；结果不等同于 QMS 放行。`
    }
  }
  return [vision, manual]
}

function inspectionDefinitions(date) {
  const updatedAt = `${date}T00:00:00.000Z`
  return [
    {
      code: 'optical.appearance.machine', version: 1, name: '光学外观视觉检查',
      description: '模拟 AOI 检查；原图必须长期保存并可复核。', updatedAt,
      characteristics: [
        { code: 'scratch_count', name: '划伤数量', inputType: 'numeric', unit: '1', upperLimit: 0, required: true },
        { code: 'edge_chip_area', name: '边缘崩缺面积', inputType: 'numeric', unit: 'mm2', upperLimit: 0.01, required: true }
      ]
    },
    {
      code: 'optical.final.manual', version: 1, name: '光学镜片人工终检',
      description: '模拟人工复核；检测结果不代表 QMS 放行。', updatedAt,
      characteristics: [
        { code: 'surface.pv', name: '面形 PV', inputType: 'numeric', unit: '1', upperLimit: 0.25, required: true },
        { code: 'visual.appearance', name: '人工外观结论', inputType: 'text', required: true }
      ]
    }
  ]
}

function qualityPlans(date) {
  const updatedAt = `${date}T00:00:00.000Z`
  return series.map((item) => ({
    planId: `optical.${item.code.toLowerCase()}.quality`,
    version: 1,
    name: `${item.code} 光学模压质量方案`,
    description: '光学玻璃模压演示模板；只对明确配置的产品系列生效。',
    status: 'published',
    priority: 100,
    effectiveFrom: updatedAt,
    effectiveTo: null,
    scope: { productSeries: item.code, productCode: null, recipeId: null, machineId: null },
    items: [
      { definitionCode: 'optical.appearance.machine', definitionVersion: 1, sequence: 10, required: true, requiresAttachment: true, requiresReview: true },
      { definitionCode: 'optical.final.manual', definitionVersion: 1, sequence: 20, required: true, requiresAttachment: false, requiresReview: false }
    ],
    updatedAt
  }))
}

function phaseDefinitions(phaseMapping, date) {
  const updatedAt = `${date}T00:00:00.000Z`
  return phaseMapping.mappings.map((item, index) => ({
    code: item.phaseCode,
    name: item.displayName,
    sortOrder: (index + 1) * 10,
    required: true,
    updatedAt
  }))
}

function phaseMappings(phaseMapping, date) {
  const updatedAt = `${date}T00:00:00.000Z`
  return series.flatMap((item) => phaseMapping.mappings.map((mapping) => ({
    mappingId: `rcp-${item.code.toLowerCase()}:*:*:${mapping.sourceStep}`,
    recipeId: `RCP-${item.code}`,
    recipeVersion: null,
    recipeTemplate: null,
    recipeStep: mapping.sourceStep,
    recipeStepName: mapping.displayName,
    phaseCode: mapping.phaseCode,
    required: true,
    phaseSource: 'recipe',
    updatedAt
  })))
}

function comparisonFeatures(acquisition, date) {
  const updatedAt = `${date}T00:00:00.000Z`
  return series.flatMap(profile => acquisition.fields.filter(item => item.useInComparison).map((item) => ({
    code: `comparison.${profile.code.toLowerCase()}.${item.code}.mean`,
    name: item.sourceField,
    phaseCode: 'cycle',
    signal: item.code,
    aggregation: 'mean',
    boundaryMode: 'strict',
    unit: item.unit,
    productSeries: profile.code,
    productCode: null,
    recipeId: `RCP-${profile.code}`,
    machineId: null,
    enabled: true,
    useInComparison: true,
    updatedAt
  })))
}

async function ensureEmptyOutput(outputDir) {
  await mkdir(outputDir, { recursive: true })
  const existing = await readdir(outputDir)
  if (existing.length > 0) {
    throw new Error(`输出目录不是空目录：${outputDir}。请指定新的 --out 目录。`)
  }
}

export async function generateFactoryDay({ date = defaultDate, hours = 8, outputDir }) {
  if (!outputDir) throw new Error('outputDir 不能为空。')
  await ensureEmptyOutput(outputDir)
  const acquisition = await readJson('acquisition-profile.v1.json')
  const recipeProfile = await readJson('recipe-profile.v1.json')
  const baseRecipe = await readJson('recipe-instance.example.json')
  const phaseMapping = await readJson('phase-mapping.v1.json')
  const sensorCodes = acquisition.fields.map(item => item.code)
  if (sensorCodes.length !== 13 || new Set(sensorCodes).size !== 13) throw new Error('采集 Profile 必须包含 13 个唯一字段。')
  if (phaseMapping.mappings.length !== 5) throw new Error('阶段映射必须包含 5 个阶段。')
  const cyclesPerMachine = hours * 3600 / cycleSeconds
  const events = []
  const cycleResults = []
  const shiftStart = new Date(`${date}T08:00:00.000Z`)
  for (let machineIndex = 0; machineIndex < machines.length; machineIndex += 1) {
    for (let cycleIndex = 0; cycleIndex < cyclesPerMachine; cycleIndex += 1) {
      const profileIndex = (cycleIndex + machineIndex) % series.length
      const result = makeCycle({
        date, machine: machines[machineIndex], machineIndex, cycleIndex,
        profile: series[profileIndex], profileIndex,
        start: new Date(shiftStart.getTime() + cycleIndex * cycleSeconds * 1000),
        acquisition, recipeProfile, baseRecipe, phaseMapping
      })
      events.push(...result.events)
      cycleResults.push(result)
    }
  }
  events.sort((a, b) =>
    a.occurredAt - b.occurredAt ||
    a.machine.id.localeCompare(b.machine.id) ||
    a.correlationId.localeCompare(b.correlationId) ||
    a.localOrder - b.localOrder
  )
  const edgeId = 'EDGE-001'
  const persistedEvents = events.map((item, index) => {
    const seq = index + 1
    return {
      eventId: uuidV7(item.occurredAt, seq),
      eventType: item.eventType,
      eventTypeVersion: 1,
      occurredAt: item.occurredAt.toISOString(),
      recordedAt: new Date(item.occurredAt.getTime() + 100).toISOString(),
      source: `edge/${edgeId}/${item.machine.plc}/optical-molding-adapter`,
      subject: { type: 'optical-molding-machine', id: item.machine.id },
      context: item.context,
      data: item.data,
      correlationId: item.correlationId,
      seq
    }
  })
  const expectedEvents = cyclesPerMachine * machines.length * 608
  if (persistedEvents.length !== expectedEvents) throw new Error(`事件数不完整：${persistedEvents.length} != ${expectedEvents}`)
  const sampleCount = persistedEvents.filter(item => item.eventType === 'process.sample').length
  const expectedSamples = cyclesPerMachine * machines.length * 600
  if (sampleCount !== expectedSamples) throw new Error(`采样数不完整：${sampleCount} != ${expectedSamples}`)
  const batchDir = join(outputDir, 'event-batches')
  const imageDir = join(outputDir, 'original-images')
  await mkdir(batchDir, { recursive: true })
  await mkdir(imageDir, { recursive: true })
  const batchSizes = []
  for (let index = 0; index < persistedEvents.length; index += batchSize) {
    const batch = { edgeId, events: persistedEvents.slice(index, index + batchSize) }
    batchSizes.push(batch.events.length)
    await writeFile(
      join(batchDir, `batch-${String(batchSizes.length).padStart(4, '0')}.json`),
      `${JSON.stringify(batch)}\n`
    )
  }
  const manifests = []
  for (let index = 0; index < cycleResults.length; index += 1) {
    const result = cycleResults[index]
    const records = inspectionRecords(result, date, 1_000_000 + index * 2)
    manifests.push(...records)
    await writeFile(join(outputDir, records[0].originalImage), bmpBuffer(result.cycle, result.visionFail))
  }
  await writeFile(join(outputDir, 'inspection-manifest.ndjson'), `${manifests.map(item => JSON.stringify(item)).join('\n')}\n`)
  await writeFile(join(outputDir, 'inspection-definitions.json'), `${JSON.stringify(inspectionDefinitions(date), null, 2)}\n`)
  await writeFile(join(outputDir, 'inspection-plans.json'), `${JSON.stringify(qualityPlans(date), null, 2)}\n`)
  await writeFile(join(outputDir, 'phase-definitions.json'), `${JSON.stringify(phaseDefinitions(phaseMapping, date), null, 2)}\n`)
  await writeFile(join(outputDir, 'phase-mappings.json'), `${JSON.stringify(phaseMappings(phaseMapping, date), null, 2)}\n`)
  await writeFile(join(outputDir, 'feature-definitions.json'), `${JSON.stringify(comparisonFeatures(acquisition, date), null, 2)}\n`)
  await writeFile(join(outputDir, 'cycles.json'), `${JSON.stringify(cycleResults.map(item => item.cycle), null, 2)}\n`)
  const seriesCycleCounts = Object.fromEntries(series.map(item => [item.code, cycleResults.filter(result => result.cycle.productSeries === item.code).length]))
  const summary = {
    simulationDate: date,
    shift: { startedAt: shiftStart.toISOString(), endedAt: new Date(shiftStart.getTime() + hours * 3600_000).toISOString(), hoursPerMachine: hours },
    edgeId,
    machineIds: machines.map(item => item.id),
    productSeries: series.map(item => item.code),
    cyclesPerMachine,
    cycleCount: cycleResults.length,
    seriesCycleCounts,
    durationSecondsPerCycle: cycleSeconds,
    sensorCountPerSample: sensorCodes.length,
    phaseCount: phaseMapping.mappings.length,
    sampleCount,
    eventCount: persistedEvents.length,
    transportBatchCount: batchSizes.length,
    transportBatchSizes: batchSizes,
    visionInspectionCount: cycleResults.length,
    manualInspectionCount: cycleResults.length,
    originalImageCount: cycleResults.length,
    outcomeCounts: {
      visionFail: cycleResults.filter(item => item.visionFail).length,
      manualFail: cycleResults.filter(item => item.manualFail).length,
      manualInconclusive: cycleResults.filter(item => item.manualInconclusive).length
    },
    anomalyCounts: Object.fromEntries(['temperature-overshoot', 'vacuum-drift', 'load-drift'].map(name => [name, cycleResults.filter(item => item.cycle.anomaly === name).length]))
  }
  await writeFile(join(outputDir, 'summary.json'), `${JSON.stringify(summary, null, 2)}\n`)
  return summary
}

const isCli = process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url
if (isCli) {
  const options = parseArgs(process.argv.slice(2))
  const summary = await generateFactoryDay(options)
  console.log(`Generated ${summary.cycleCount} cycles and ${summary.eventCount} events in ${options.outputDir}`)
  console.log(`Machines: ${summary.machineIds.join(', ')}; series: ${JSON.stringify(summary.seriesCycleCounts)}`)
  console.log(`Samples: ${summary.sampleCount}; batches: ${summary.transportBatchCount}; original images: ${summary.originalImageCount}`)
}
