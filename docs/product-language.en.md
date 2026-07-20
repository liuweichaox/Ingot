# Product Language

Ingot is written for production, process, quality, equipment, and plant IT teams. Pages, API documentation, and public copy use everyday manufacturing terms instead of internal software terminology.

## Writing rules

- Name the user's task: “query a production cycle,” “compare parameters,” or “upload an inspection attachment.”
- Name plant data directly: equipment parameters, inspection results, production records, and process stages.
- Distinguish machine collection, manual entry, and system estimation with a specific business label.
- Present analysis progressively: data completeness, observed changes, parameter relationships, possible causes, then process recommendations.
- When data is incomplete, name the missing parameter, inspection result, batch field, or cycle record.
- Analyze only core parameters that directly affect the process or quality; do not introduce unavailable shift, organization, or individual-performance fields for convenience.

## Preferred terms

Use “production record” or “production history” for stored plant data, “related production records” for links behind an analysis result, and “inspection attachment” for uploaded photos or files. Use “user,” “inspection operator,” or “workstation” for identity. Use “possible cause” for an unconfirmed diagnosis and “opposing result” or “review result” when the data does not support it.

Public JSON follows the same rule: `attachments` for inspection files, `relatedRecords` for records linked from analysis, and `userId` for the signed-in identity.
