import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'

const file = fileURLToPath(new URL('./platform-configuration.json', import.meta.url))
const configuration = JSON.parse(await readFile(file, 'utf8'))
const apiArg = process.argv.find((item) => item.startsWith('--api='))
const api = (apiArg?.slice('--api='.length) ?? 'http://127.0.0.1:8000').replace(/\/$/, '')

async function publish(path, value) {
  const response = await fetch(`${api}${path}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(value)
  })
  if (!response.ok) {
    throw new Error(`${path} ${response.status}: ${await response.text()}`)
  }
  return response.json()
}

for (const model of configuration.dataModels) {
  await publish('/api/v1/process-data-models', model)
}
for (const recipe of configuration.recipeVersions) {
  await publish('/api/v1/recipe-versions', recipe)
}
for (const plan of configuration.analysisPlans) {
  await publish('/api/v1/process-analysis-plans', plan)
}

process.stdout.write(JSON.stringify({
  platform: api,
  dataModels: configuration.dataModels.length,
  recipeVersions: configuration.recipeVersions.length,
  analysisPlans: configuration.analysisPlans.length
}, null, 2) + '\n')
