# Semiconductor Wafer Etch Acquisition Demo (Retired)

## Purpose

The former demo used a Modbus simulator to show synthetic process data. Ingot now receives events from external adapters; the former equipment driver and demo workflow are retired.

## Start

The repository no longer provides the former demo startup workflow. See [Data integration](data-connection.en.md) for connecting external data.

## Verify

Validate data identity, timestamps, units, run association, and recovery in the target adapter environment; demo data cannot prove real-device interoperability or process results.

## Configuration boundary

External adapters own field equipment protocols, acquisition triggers, and equipment writes. Edge only receives, persists, and delivers events.
