<div align="center">
  <a href="https://ingotstack.com">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="images/logo/ingot-lockup-dark.svg">
      <img src="images/logo/ingot-lockup.svg" alt="Ingot" width="360">
    </picture>
  </a>

  <h3>Production data and process analysis for manufacturing operations</h3>

  <p>
    Connect equipment processes, batches, workpieces, recipes, tooling, and inspections into a continuous production history,<br>
    helping engineers investigate yield changes, machine differences, tooling trends, and abnormal workpieces.
  </p>

  <p>
    <a href="https://ingotstack.com/en/"><strong>Website</strong></a>
    ·
    <a href="https://docs.ingotstack.com/en"><strong>Documentation</strong></a>
    ·
    <a href="https://github.com/liuweichaox/Ingot/issues">Feedback</a>
  </p>

  <p>English · <a href="README.md">简体中文</a></p>
</div>

## What is Ingot?

Manufacturing data is often scattered across equipment, instruments, production systems, inspection systems, and engineering spreadsheets. Ingot links these records around the production cycle to create a production history that teams can query, compare, and trace.

An engineer can start from a batch, an abnormal workpiece, or an everyday-language question and review the matching equipment, recipe, tooling, process stages, parameters, and inspection outcomes. Metrics and charts in an analysis open the related production records.

## Core capabilities

| Capability | Outcome |
|---|---|
| Plant data collection | Continuously collect equipment parameters, state changes, and inspection data with production context |
| Production history | Link process and outcome from the same run across batches, workpieces, and production cycles |
| Process configuration | Maintain recipe versions, process stages, parameter units, tooling, and quality plans |
| Cycle and batch analysis | Compare similar cycles and locate differences across batches, machines, stages, and parameters |
| Ingot Chat | Start investigations in everyday language and receive summaries, metrics, charts, and related records |
| Analysis plans | Preserve target metrics, comparison dimensions, and filters as reusable investigation methods |

## Use cases

### Why did yield change?

Compare a target batch with a historical baseline to see where the change began, which station or process stage concentrates the difference, and which process parameters changed with it.

### Why do machines differ on the same recipe?

Compare cycle time, key parameters, inspection distributions, and anomaly frequency across machines running the same product and recipe.

### When should tooling be serviced?

Follow process features and quality metrics across usage count or accumulated operating time to support maintenance planning.

### What happened to one abnormal workpiece?

Move from an inspection outcome to the matching batch, equipment, recipe version, tooling, and complete process curve, then compare it with normal workpieces from the same batch.

## How the product works

```text
equipment, instruments, and plant data
                 │
                 ▼
      Edge · collection and buffering
                 │
                 ▼
 Platform · production history and process configuration
                 │
                 ├── batch, cycle, workpiece, and inspection views
                 ├── stages, recipes, tooling, and analysis plans
                 └── Ingot Chat · process investigation and drill-down
```

An investigation normally follows five steps:

1. bring equipment processes, production objects, and inspections together;
2. establish the business meaning of recipes, tooling, stages, and parameters;
3. organize data from the same run into a production history;
4. check data quality and compare similar production cycles;
5. move from the analysis summary into curves, inspections, and production context for review.

## Learn about the project

- [Product overview](docs/product-overview.en.md)
- [Use cases](docs/use-cases.en.md)
- [How Ingot works](docs/how-it-works.en.md)
- [Ingot Chat](docs/ingot-chat.en.md)
- [Rollout](docs/rollout.en.md)
- [FAQ](docs/faq.en.md)

## Participate

Use Issues to share plant problems, product feedback, and improvement ideas. For code contributions, see the [contributing guide](CONTRIBUTING.en.md). Report security concerns through the [security policy](SECURITY.md).

Ingot is available under the [MIT License](LICENSE).
