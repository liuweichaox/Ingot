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
  } finally {
    await rm(outputDir, { recursive: true, force: true })
  }
})
