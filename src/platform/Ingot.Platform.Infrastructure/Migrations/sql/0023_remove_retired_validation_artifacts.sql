-- Remove the retired cross-project validation workflow and its remaining evidence.
DELETE FROM research_evidence
WHERE kind = 'transfer-assessment'
   OR resource_type = 'transfer-assessment';

DELETE FROM research_hypothesis_evidence
WHERE kind = 'transfer-assessment';

DELETE FROM mechanism_claim_evidence
WHERE evidence_kind = 'transfer-assessment';

DELETE FROM mechanism_claim_lifecycle_decisions
WHERE evidence_kind = 'transfer-assessment';

DELETE FROM process_research_audit
WHERE resource_type = 'transfer-assessment'
   OR payload->>'resourceType' = 'transfer-assessment';

CREATE FUNCTION pg_temp.without_transfer_assessment_evidence(document jsonb)
RETURNS jsonb
LANGUAGE sql
IMMUTABLE
STRICT
AS $function$
    SELECT CASE
        WHEN jsonb_typeof(document->'evidence') = 'array' THEN
            jsonb_set(
                document,
                ARRAY['evidence'],
                COALESCE(
                    (
                        SELECT jsonb_agg(evidence_item.value)
                        FROM jsonb_array_elements(document->'evidence')
                            AS evidence_item(value)
                        WHERE COALESCE(
                                  evidence_item.value->>'kind',
                                  evidence_item.value->>'Kind')
                              IS DISTINCT FROM 'transfer-assessment'
                    ),
                    '[]'::jsonb),
                false)
        ELSE document
    END
$function$;

UPDATE research_knowledge_claims
SET payload = pg_temp.without_transfer_assessment_evidence(
    payload - 'transferAssessmentId' - 'transfer_assessment_id');

UPDATE research_operating_regions
SET payload = pg_temp.without_transfer_assessment_evidence(payload);

ALTER TABLE research_evidence
    DROP CONSTRAINT research_evidence_kind_check;

ALTER TABLE research_evidence
    ADD CONSTRAINT research_evidence_kind_check
    CHECK (kind = ANY (ARRAY[
        'dataset-snapshot',
        'analysis-run',
        'execution-comparison',
        'mechanism-model',
        'knowledge-source',
        'operating-region'
    ]));

DROP TABLE research_transfer_assessments;
DROP TABLE research_rollback_drills;
