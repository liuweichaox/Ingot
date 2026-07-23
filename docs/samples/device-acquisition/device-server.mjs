import { createServer } from 'node:http'
import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'

const configPath = fileURLToPath(new URL('./device-config.json', import.meta.url))
const config = JSON.parse(await readFile(configPath, 'utf8'))
const portArg = process.argv.find((item) => item.startsWith('--port='))
const port = Number(portArg?.slice('--port='.length) ?? process.env.DEVICE_SIMULATOR_PORT ?? 8100)
if (!Number.isInteger(port) || port < 1 || port > 65535) {
  throw new Error(`无效端口：${port}`)
}

let activeRecipeId = config.initialRecipeId
let sequence = 0
const startedAt = Date.now()

function activeRecipe() {
  return config.recipes.find((item) => item.id === activeRecipeId)
}

function snapshot() {
  sequence += 1
  const elapsedSeconds = (Date.now() - startedAt) / 1000
  const recipe = activeRecipe()
  const setpoint = recipe.parameters['目标温度℃']
  const warmup = Math.min(1, elapsedSeconds / 90)
  const temperature = 25 + (setpoint - 25) * warmup + Math.sin(elapsedSeconds / 4) * 1.8
  const pressure = -1.8 + Math.sin(elapsedSeconds / 9) * 0.15
  const oxygen = recipe.parameters['保护气启用']
    ? 38 + Math.cos(elapsedSeconds / 7) * 4
    : 210000
  const heaterEnabled = temperature < setpoint - 2

  return {
    deviceId: config.device.id,
    timestamp: new Date().toISOString(),
    sequence,
    productSeries: config.device.productSeries,
    operatingState: 'running',
    activeRecipe: recipe,
    sensors: {
      '炉温℃': Number(temperature.toFixed(2)),
      '设定温度℃': setpoint,
      '炉压kPa': Number(pressure.toFixed(3)),
      '氧含量ppm': Number(oxygen.toFixed(2)),
      '风机转速rpm': 1450 + Math.round(Math.sin(elapsedSeconds / 5) * 20),
      '加热器开启': heaterEnabled,
      '运行模式': recipe.parameters['工艺模式']
    }
  }
}

function json(response, status, body) {
  response.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'cache-control': 'no-store'
  })
  response.end(JSON.stringify(body))
}

async function readJson(request) {
  const chunks = []
  for await (const chunk of request) chunks.push(chunk)
  return JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}')
}

const server = createServer(async (request, response) => {
  const url = new URL(request.url, `http://${request.headers.host}`)
  if (request.method === 'GET' && url.pathname === '/health') {
    return json(response, 200, { status: 'healthy', deviceId: config.device.id })
  }
  if (request.method === 'GET' && url.pathname === '/api/v1/device') {
    return json(response, 200, { ...config.device, startedAt: new Date(startedAt).toISOString() })
  }
  if (request.method === 'GET' && url.pathname === '/api/v1/recipes') {
    return json(response, 200, { activeRecipeId, data: config.recipes })
  }
  if (request.method === 'GET' && url.pathname === '/api/v1/snapshot') {
    return json(response, 200, snapshot())
  }
  if (request.method === 'PUT' && url.pathname === '/api/v1/active-recipe') {
    try {
      const body = await readJson(request)
      if (!config.recipes.some((item) => item.id === body.recipeId)) {
        return json(response, 404, { error: `配方不存在：${body.recipeId ?? ''}` })
      }
      activeRecipeId = body.recipeId
      return json(response, 200, { activeRecipe: activeRecipe() })
    } catch (error) {
      return json(response, 400, { error: error.message })
    }
  }
  return json(response, 404, { error: 'not_found' })
})

server.listen(port, '127.0.0.1', () => {
  process.stdout.write(`Device simulator ${config.device.id} listening on http://127.0.0.1:${port}\n`)
})

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => server.close(() => process.exit(0)))
}
