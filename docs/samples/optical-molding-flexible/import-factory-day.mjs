import { readFile, readdir } from 'node:fs/promises'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = dirname(fileURLToPath(import.meta.url))

function parseArgs(argv) {
  const value = (name, fallback) => {
    const index = argv.indexOf(name)
    return index >= 0 ? argv[index + 1] : fallback
  }
  const directory = resolve(value('--dir', join(root, 'generated', 'factory-day-2026-07-20')))
  return {
    directory,
    api: value('--api', 'http://127.0.0.1:8000').replace(/\/$/, ''),
    edgeToken: value('--edge-token', process.env.INGOT_EDGE_TOKEN ?? ''),
    platformToken: value('--platform-token', process.env.INGOT_PLATFORM_TOKEN ?? ''),
    skipEvents: argv.includes('--skip-events'),
    skipInspections: argv.includes('--skip-inspections')
  }
}

async function responseJson(response) {
  const text = await response.text()
  const payload = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}: ${text}`)
  return payload
}

async function postJson(url, body, token) {
  const headers = { 'Content-Type': 'application/json' }
  if (token) headers.Authorization = `Bearer ${token}`
  return responseJson(await fetch(url, { method: 'POST', headers, body: JSON.stringify(body) }))
}

async function getJson(url, token) {
  const headers = token ? { Authorization: `Bearer ${token}` } : {}
  return responseJson(await fetch(url, { headers }))
}

async function exists(url, token) {
  const headers = token ? { Authorization: `Bearer ${token}` } : {}
  const response = await fetch(url, { headers })
  if (response.status === 404) return false
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}: ${await response.text()}`)
  return true
}

async function importEvents(options) {
  const batchDir = join(options.directory, 'event-batches')
  const files = (await readdir(batchDir)).filter(name => name.endsWith('.json')).sort()
  let accepted = 0
  let duplicates = 0
  for (let index = 0; index < files.length; index += 1) {
    const batch = JSON.parse(await readFile(join(batchDir, files[index]), 'utf8'))
    const result = await postJson(`${options.api}/api/v1/events:batch`, batch, options.edgeToken)
    accepted += result.accepted ?? 0
    duplicates += result.duplicates ?? 0
    if ((index + 1) % 10 === 0 || index + 1 === files.length) {
      console.log(`Events ${index + 1}/${files.length}: accepted=${accepted}, duplicates=${duplicates}`)
    }
  }
}

async function uploadOriginal(options, relativePath) {
  const absolutePath = join(options.directory, relativePath)
  const data = await readFile(absolutePath)
  const form = new FormData()
  form.append('file', new Blob([data], { type: 'image/bmp' }), basename(relativePath))
  const headers = options.platformToken ? { Authorization: `Bearer ${options.platformToken}` } : {}
  return responseJson(await fetch(`${options.api}/api/v1/inspection-attachments`, { method: 'POST', headers, body: form }))
}

async function importInspections(options) {
  const lines = (await readFile(join(options.directory, 'inspection-manifest.ndjson'), 'utf8')).trim().split(/\r?\n/)
  let created = 0
  let skipped = 0
  for (let index = 0; index < lines.length; index += 1) {
    const item = JSON.parse(lines[index])
    if (await exists(`${options.api}/api/v1/inspection-records/${item.record.recordId}`, options.platformToken)) {
      skipped += 1
      continue
    }
    if (item.originalImage) {
      item.record.attachments = [await uploadOriginal(options, item.originalImage)]
    }
    await postJson(`${options.api}/api/v1/inspection-records`, item.record, options.platformToken)
    created += 1
    if ((index + 1) % 20 === 0 || index + 1 === lines.length) {
      console.log(`Inspections ${index + 1}/${lines.length}: created=${created}, skipped=${skipped}`)
    }
  }
}

async function importConfiguration(options) {
  const resources = [
    ['process-data-models.json', 'process-data-models'],
    ['recipe-versions.json', 'recipe-versions'],
    ['process-analysis-plans.json', 'process-analysis-plans'],
    ['inspection-definitions.json', 'inspection-definitions'],
    ['inspection-plans.json', 'inspection-plans']
  ]
  for (const [fileName, endpoint] of resources) {
    const items = JSON.parse(await readFile(join(options.directory, fileName), 'utf8'))
    for (const item of items) {
      await postJson(`${options.api}/api/v1/${endpoint}`, item, options.platformToken)
    }
  }
}

async function importManufacturingSetup(options) {
  const setup = JSON.parse(await readFile(join(options.directory, 'manufacturing-setup.json'), 'utf8'))
  for (const item of setup.componentTypes ?? []) {
    await postJson(`${options.api}/api/v1/tooling-component-types`, item, options.platformToken)
  }
  const types = (await getJson(`${options.api}/api/v1/tooling-types`, options.platformToken)).data ?? []
  const typeKeys = new Set(types.map(item => `${item.toolingTypeCode}@${item.version}`))
  for (const item of setup.toolingTypes) {
    if (!typeKeys.has(`${item.toolingTypeCode}@${item.version}`)) {
      await postJson(`${options.api}/api/v1/tooling-types`, item, options.platformToken)
    }
  }
  for (const item of setup.components) {
    await postJson(`${options.api}/api/v1/tooling-components`, item, options.platformToken)
  }
  for (const item of setup.assemblies) {
    await postJson(`${options.api}/api/v1/tooling-assemblies`, item, options.platformToken)
  }

  const revisions = (await getJson(`${options.api}/api/v1/tooling-assemblies/revisions`, options.platformToken)).data ?? []
  const revisionIds = new Set(revisions.map(item => item.assemblyRevisionId))
  for (const item of setup.revisions) {
    if (!revisionIds.has(item.assemblyRevisionId)) {
      await postJson(
        `${options.api}/api/v1/tooling-assemblies/${encodeURIComponent(item.moldId)}/revisions`,
        item,
        options.platformToken
      )
    }
  }

  const installations = (await getJson(`${options.api}/api/v1/tooling-installations`, options.platformToken)).data ?? []
  const installationIds = new Set(installations.map(item => item.installationId))
  for (const item of setup.installations) {
    if (!installationIds.has(item.installationId)) {
      await postJson(`${options.api}/api/v1/tooling-installations`, item, options.platformToken)
    }
  }

  const contexts = (await getJson(`${options.api}/api/v1/production-contexts`, options.platformToken)).data ?? []
  const contextIds = new Set(contexts.map(item => item.contextId))
  for (const item of setup.productionContexts) {
    if (!contextIds.has(item.contextId)) {
      await postJson(`${options.api}/api/v1/production-contexts`, item, options.platformToken)
    }
  }
  console.log(`Manufacturing setup: ${setup.components.length} components, ${setup.revisions.length} revisions, ${setup.installations.length} installations, ${setup.productionContexts.length} contexts`)
}

const options = parseArgs(process.argv.slice(2))
const health = await fetch(`${options.api}/health`)
if (!health.ok) throw new Error(`Platform API 不可用：${options.api}/health`)
await importConfiguration(options)
await importManufacturingSetup(options)
if (!options.skipEvents) await importEvents(options)
if (!options.skipInspections) await importInspections(options)
console.log('Factory-day import completed.')
