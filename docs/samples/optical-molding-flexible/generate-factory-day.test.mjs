import assert from 'node:assert/strict'
import { mkdtemp, readFile, readdir, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import test from 'node:test'

import { generateFactoryDay } from './generate-factory-day.mjs'

test('generates two machines, three product series and complete cycle data', async () => {
  const outputDir = await mkdtemp(join(tmpdir(), 'ingot-factory-day-'))
  try {
    const summary = await generateFactoryDay({ date: '2026-07-20', hours: 0.5, outputDir })
    assert.equal(summary.machineIds.length, 2)
    assert.equal(summary.productSeries.length, 3)
    assert.equal(summary.cyclesPerMachine, 3)
    assert.equal(summary.cycleCount, 6)
    assert.deepEqual(summary.seriesCycleCounts, { 'LENS-A': 2, 'LENS-B': 2, 'LENS-C': 2 })
    assert.equal(summary.sampleCount, 3600)
    assert.equal(summary.eventCount, 3648)
    assert.equal(summary.visionInspectionCount, 6)
    assert.equal(summary.manualInspectionCount, 6)
    assert.equal(summary.originalImageCount, 6)
    assert.equal(summary.toolingComponentCount, 12)
    assert.equal(summary.toolingAssemblyCount, 3)
    assert.equal(summary.toolingInstallationCount, 6)
    assert.equal(summary.productionContextCount, 6)

    const batchFiles = (await readdir(join(outputDir, 'event-batches'))).sort()
    assert.equal(batchFiles.length, 8)
    const events = []
    for (const file of batchFiles) {
      const batch = JSON.parse(await readFile(join(outputDir, 'event-batches', file), 'utf8'))
      assert.equal(batch.edgeId, 'EDGE-001')
      assert.ok(batch.events.length <= 500)
      events.push(...batch.events)
    }
    assert.deepEqual(events.map(item => item.seq), Array.from({ length: 3648 }, (_, index) => index + 1))
    assert.deepEqual([...new Set(events.map(item => item.subject.id))].sort(), ['GLASS-PRESS-01', 'GLASS-PRESS-02'])
    const sample = events.find(item => item.eventType === 'process.sample')
    assert.equal(Object.keys(sample.data.values).length, 13)

    const manifest = (await readFile(join(outputDir, 'inspection-manifest.ndjson'), 'utf8')).trim().split(/\r?\n/).map(JSON.parse)
    assert.equal(manifest.length, 12)
    assert.equal(manifest.filter(item => item.kind === 'vision').length, 6)
    assert.equal(manifest.filter(item => item.kind === 'manual').length, 6)
    const image = await readFile(join(outputDir, manifest.find(item => item.originalImage).originalImage))
    assert.equal(image.subarray(0, 2).toString('ascii'), 'BM')

    const setup = JSON.parse(await readFile(join(outputDir, 'manufacturing-setup.json'), 'utf8'))
    assert.deepEqual(setup.componentTypes.map(item => item.name).sort(), ['模架', '模芯'])
    assert.equal(setup.toolingTypes[0].roles.length, 4)
    assert.equal(setup.components.length, 12)
    assert.deepEqual([...new Set(setup.components.map(item => item.componentTypeCode))].sort(), ['mold_core', 'mold_holder'])
    assert.deepEqual([...new Set(setup.components.map(item => item.attributes.componentTypeName))].sort(), ['模架', '模芯'])
    assert.equal(setup.revisions.length, 3)
    assert.equal(setup.installations.length, 6)
    assert.equal(setup.productionContexts.length, 6)
    assert.ok(setup.productionContexts.every(item => item.toolingInstallationId))

    const models = JSON.parse(await readFile(join(outputDir, 'process-data-models.json'), 'utf8'))
    const recipes = JSON.parse(await readFile(join(outputDir, 'recipe-versions.json'), 'utf8'))
    const plans = JSON.parse(await readFile(join(outputDir, 'process-analysis-plans.json'), 'utf8'))
    assert.equal(models[0].acquisition.dataItems.length, 13)
    assert.equal(models[0].recipeParameters.length, 31)
    assert.ok(models[0].recipeParameters.every(item => !('value' in item)))
    assert.equal(recipes.length, 6)
    assert.ok(recipes.every(recipe => recipe.values.length === 31))
    assert.ok(recipes.every(item => item.values.length === 31))
    assert.equal(plans[0].signals.length, 5)
    assert.equal(plans[0].cohortDimension, 'quality.outcome')
  } finally {
    await rm(outputDir, { recursive: true, force: true })
  }
})
