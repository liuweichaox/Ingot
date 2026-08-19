-- 0001_current_schema.sql
-- Fresh-install schema for the current product. This baseline contains only the current product schema.

CREATE EXTENSION IF NOT EXISTS timescaledb;

SET default_table_access_method = heap;

--
-- Name: agent_runs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.agent_runs (
    run_id text NOT NULL,
    user_id text NOT NULL,
    entry_point text NOT NULL,
    status text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    completed_at timestamp with time zone,
    snapshot jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


--
-- Name: agent_stream_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.agent_stream_events (
    sequence bigint NOT NULL,
    run_id text NOT NULL,
    event_type text NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    data jsonb
);


--
-- Name: agent_stream_events_sequence_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.agent_stream_events ALTER COLUMN sequence ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.agent_stream_events_sequence_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: case_level_evaluations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.case_level_evaluations (
    evaluation_id uuid NOT NULL,
    case_id uuid NOT NULL,
    evaluated_at timestamp with time zone DEFAULT now() NOT NULL,
    level text NOT NULL,
    gates jsonb NOT NULL,
    window_days integer DEFAULT 14 NOT NULL
);


--
-- Name: collection_points; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.collection_points (
    collection_point_id text NOT NULL,
    site_id text NOT NULL,
    edge_id text NOT NULL,
    subject_type text NOT NULL,
    subject_id text NOT NULL,
    signal_code text NOT NULL,
    static_tags jsonb DEFAULT '{}'::jsonb NOT NULL,
    first_seen_at timestamp with time zone NOT NULL,
    last_seen_at timestamp with time zone NOT NULL,
    point_key bigint NOT NULL,
    CONSTRAINT collection_points_site_id_check CHECK ((site_id ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$'::text))
);


--
-- Name: collection_points_point_key_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.collection_points ALTER COLUMN point_key ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.collection_points_point_key_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: data_object_operation_keys; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.data_object_operation_keys (
    site_id text NOT NULL,
    subject_type text NOT NULL,
    subject_id text NOT NULL,
    execution_id text NOT NULL,
    CONSTRAINT data_object_operation_keys_site_id_check CHECK ((site_id ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$'::text))
);


--
-- Name: data_object_summaries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.data_object_summaries (
    site_id text NOT NULL,
    subject_type text NOT NULL,
    subject_id text NOT NULL,
    edge_id text,
    event_count bigint DEFAULT 0 NOT NULL,
    sample_count bigint DEFAULT 0 NOT NULL,
    operation_count bigint DEFAULT 0 NOT NULL,
    first_observed_at timestamp with time zone,
    last_observed_at timestamp with time zone,
    last_sample_at timestamp with time zone,
    maximum_sample_gap_seconds double precision,
    latest_event_type text,
    context jsonb DEFAULT '{}'::jsonb NOT NULL,
    latest_ingest_id bigint DEFAULT 0 NOT NULL,
    CONSTRAINT data_object_summaries_site_id_check CHECK ((site_id ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$'::text))
);


--
-- Name: data_source_instances; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.data_source_instances (
    data_source_id text NOT NULL,
    version integer NOT NULL,
    edge_id text NOT NULL,
    status text NOT NULL,
    protocol text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT data_source_instances_version_check CHECK ((version > 0))
);


--
-- Name: dataset_quality_validation_reports; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.dataset_quality_validation_reports (
    report_id uuid NOT NULL,
    dataset_id text NOT NULL,
    dataset_version integer NOT NULL,
    industry text NOT NULL,
    status text NOT NULL,
    source_sha256 text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT dataset_quality_validation_reports_dataset_version_check CHECK ((dataset_version > 0)),
    CONSTRAINT dataset_quality_validation_reports_status_check CHECK ((status = ANY (ARRAY['passed'::text, 'rejected'::text])))
);


--
-- Name: edge_runtime_status_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.edge_runtime_status_history (
    edge_id text NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    acquisition_state text,
    last_valid_snapshot_at timestamp with time zone,
    valid_snapshot_count bigint NOT NULL,
    emitted_event_count bigint NOT NULL,
    acquisition_error text,
    delivery_state text,
    pending_event_count bigint NOT NULL,
    oldest_pending_event_at timestamp with time zone,
    backlog_capacity_used_percent double precision,
    shipment_rate_per_second double precision,
    delivery_error text
);


--
-- Name: event_ingest_keys; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.event_ingest_keys (
    event_id text NOT NULL,
    site_id text NOT NULL,
    edge_id text NOT NULL,
    seq bigint NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    payload_hash text NOT NULL,
    CONSTRAINT event_ingest_keys_payload_hash_check CHECK ((payload_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT event_ingest_keys_site_id_check CHECK ((site_id ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$'::text))
);


--
-- Name: execution_analysis_backfill_jobs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.execution_analysis_backfill_jobs (
    job_id uuid NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    available_at timestamp with time zone DEFAULT now() NOT NULL,
    attempt_count integer DEFAULT 0 NOT NULL,
    lease_id uuid,
    leased_at timestamp with time zone,
    CONSTRAINT execution_analysis_backfill_jobs_attempt_count_check CHECK ((attempt_count >= 0)),
    CONSTRAINT execution_analysis_backfill_jobs_lease_check CHECK (((status = 'running'::text) = ((lease_id IS NOT NULL) AND (leased_at IS NOT NULL)))),
    CONSTRAINT execution_analysis_backfill_jobs_status_check CHECK ((status = ANY (ARRAY['queued'::text, 'running'::text, 'completed'::text, 'completed_with_errors'::text, 'failed'::text])))
);


--
-- Name: execution_analysis_materializations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.execution_analysis_materializations (
    execution_id text NOT NULL,
    algorithm_version text NOT NULL,
    data_model_id text NOT NULL,
    data_model_version integer NOT NULL,
    analysis_plan_id text NOT NULL,
    analysis_plan_version integer NOT NULL,
    source_max_ingest_id bigint NOT NULL,
    source_event_count integer NOT NULL,
    status text NOT NULL,
    computed_at timestamp with time zone NOT NULL,
    invalidated_at timestamp with time zone,
    invalidated_source_max_ingest_id bigint DEFAULT 0 NOT NULL,
    invalidation_reason text,
    result jsonb NOT NULL,
    source_min_ingest_id bigint DEFAULT 0 NOT NULL,
    source_content_hash text DEFAULT ''::text NOT NULL
);


--
-- Name: execution_analysis_recompute_jobs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.execution_analysis_recompute_jobs (
    execution_id text NOT NULL,
    invalidated_source_max_ingest_id bigint NOT NULL,
    reason text NOT NULL,
    status text NOT NULL,
    available_at timestamp with time zone DEFAULT now() NOT NULL,
    attempt_count integer DEFAULT 0 NOT NULL,
    lease_id uuid,
    leased_at timestamp with time zone,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT execution_analysis_recompute_jobs_attempt_count_check CHECK ((attempt_count >= 0)),
    CONSTRAINT execution_analysis_recompute_jobs_check CHECK (((status = 'running'::text) = ((lease_id IS NOT NULL) AND (leased_at IS NOT NULL)))),
    CONSTRAINT execution_analysis_recompute_jobs_status_check CHECK ((status = ANY (ARRAY['queued'::text, 'running'::text])))
);


--
-- Name: execution_features; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.execution_features (
    execution_id text NOT NULL,
    algorithm_version text NOT NULL,
    data_model_id text NOT NULL,
    data_model_version integer NOT NULL,
    analysis_plan_id text NOT NULL,
    analysis_plan_version integer NOT NULL,
    signal_code text NOT NULL,
    signal_name text NOT NULL,
    signal_unit text,
    signal_sample_count integer NOT NULL,
    phase_code text NOT NULL,
    phase_name text,
    phase_order integer NOT NULL,
    phase_source text NOT NULL,
    feature_code text NOT NULL,
    feature_definition_version integer DEFAULT 1 NOT NULL,
    feature_definition_hash text DEFAULT ''::text NOT NULL,
    computation_hash text DEFAULT ''::text NOT NULL,
    input_point_count integer DEFAULT 0 NOT NULL,
    feature_value double precision,
    valid_duration_ms double precision NOT NULL,
    coverage double precision NOT NULL,
    started_at timestamp with time zone,
    ended_at timestamp with time zone
);


--
-- Name: execution_phases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.execution_phases (
    execution_id text NOT NULL,
    algorithm_version text NOT NULL,
    data_model_id text NOT NULL,
    data_model_version integer NOT NULL,
    analysis_plan_id text NOT NULL,
    analysis_plan_version integer NOT NULL,
    phase_code text NOT NULL,
    phase_name text NOT NULL,
    phase_order integer NOT NULL,
    phase_source text NOT NULL,
    required boolean NOT NULL,
    is_complete boolean NOT NULL,
    sample_count integer NOT NULL,
    started_at timestamp with time zone,
    ended_at timestamp with time zone
);


--
-- Name: feature_definitions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.feature_definitions (
    code text NOT NULL,
    phase_code text NOT NULL,
    signal text NOT NULL,
    aggregation text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


--
-- Name: golden_question_cases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.golden_question_cases (
    case_id uuid NOT NULL,
    version integer NOT NULL,
    status text NOT NULL,
    question text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT golden_question_cases_status_check CHECK ((status = ANY (ARRAY['draft'::text, 'reviewed'::text, 'retired'::text]))),
    CONSTRAINT golden_question_cases_version_check CHECK ((version > 0))
);


--
-- Name: golden_question_evaluations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.golden_question_evaluations (
    evaluation_id uuid NOT NULL,
    case_id uuid NOT NULL,
    case_version integer NOT NULL,
    agent_run_id text NOT NULL,
    passed boolean NOT NULL,
    payload jsonb NOT NULL,
    evaluated_at timestamp with time zone NOT NULL,
    agent_run_snapshot jsonb,
    agent_run_snapshot_hash text,
    CONSTRAINT ck_golden_question_evaluation_snapshot_pair CHECK ((((agent_run_snapshot IS NULL) AND (agent_run_snapshot_hash IS NULL)) OR ((agent_run_snapshot IS NOT NULL) AND (agent_run_snapshot_hash IS NOT NULL)))),
    CONSTRAINT golden_question_evaluations_agent_run_snapshot_hash_check CHECK (((agent_run_snapshot_hash IS NULL) OR (agent_run_snapshot_hash ~ '^[a-f0-9]{64}$'::text)))
);


--
-- Name: ingestion_task_bindings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ingestion_task_bindings (
    task_id text NOT NULL,
    version integer NOT NULL,
    template_id text NOT NULL,
    template_version integer NOT NULL,
    data_source_id text NOT NULL,
    data_source_version integer NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT ingestion_task_bindings_data_source_version_check CHECK ((data_source_version > 0)),
    CONSTRAINT ingestion_task_bindings_template_version_check CHECK ((template_version > 0)),
    CONSTRAINT ingestion_task_bindings_version_check CHECK ((version > 0))
);


--
-- Name: ingestion_task_templates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ingestion_task_templates (
    template_id text NOT NULL,
    version integer NOT NULL,
    status text NOT NULL,
    protocol text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT ingestion_task_templates_version_check CHECK ((version > 0))
);


--
-- Name: ingestion_tasks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ingestion_tasks (
    task_id text NOT NULL,
    version integer NOT NULL,
    edge_id text NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT ingestion_tasks_version_check CHECK ((version > 0))
);


--
-- Name: inspection_attachments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inspection_attachments (
    attachment_id uuid NOT NULL,
    storage_ref text NOT NULL,
    sha256 text NOT NULL,
    media_type text NOT NULL,
    file_name text NOT NULL,
    size_bytes bigint NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT inspection_attachments_size_bytes_check CHECK ((size_bytes > 0))
);


--
-- Name: inspection_audit_log; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inspection_audit_log (
    audit_id bigint NOT NULL,
    inspection_record_id uuid,
    attachment_id uuid,
    action text NOT NULL,
    occurred_at timestamp with time zone DEFAULT now() NOT NULL,
    actor text NOT NULL,
    detail text
);


--
-- Name: inspection_audit_log_audit_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.inspection_audit_log_audit_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: inspection_audit_log_audit_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.inspection_audit_log_audit_id_seq OWNED BY public.inspection_audit_log.audit_id;


--
-- Name: inspection_definitions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inspection_definitions (
    code text NOT NULL,
    version integer NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT inspection_definitions_version_check CHECK ((version > 0))
);


--
-- Name: inspection_plans; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inspection_plans (
    plan_id text NOT NULL,
    version integer NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT inspection_plans_version_check CHECK ((version > 0))
);


--
-- Name: inspection_records; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inspection_records (
    record_id uuid NOT NULL,
    output_item_id text,
    execution_id text NOT NULL,
    definition_code text NOT NULL,
    definition_version integer NOT NULL,
    measured_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    ingested_at timestamp with time zone DEFAULT now() NOT NULL,
    outcome text NOT NULL,
    submitted_by text NOT NULL,
    submitter_verified boolean NOT NULL,
    instrument jsonb,
    measurements jsonb DEFAULT '[]'::jsonb NOT NULL,
    attachments jsonb DEFAULT '[]'::jsonb NOT NULL,
    notes text,
    supersedes_record_id uuid,
    correction_reason text,
    payload_hash text NOT NULL,
    CONSTRAINT inspection_records_definition_version_check CHECK ((definition_version > 0)),
    CONSTRAINT inspection_records_outcome_check CHECK ((outcome = ANY (ARRAY['PASS'::text, 'FAIL'::text, 'INCONCLUSIVE'::text])))
);


--
-- Name: inspection_reviews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inspection_reviews (
    review_id uuid NOT NULL,
    inspection_record_id uuid NOT NULL,
    execution_id text NOT NULL,
    decision text NOT NULL,
    reviewed_at timestamp with time zone DEFAULT now() NOT NULL,
    reviewed_by text NOT NULL,
    notes text,
    payload_hash text NOT NULL,
    CONSTRAINT inspection_reviews_decision_check CHECK ((decision = ANY (ARRAY['CONFIRMED'::text, 'REJECTED'::text, 'REINSPECTION_REQUIRED'::text])))
);


--
-- Name: inspection_scopes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inspection_scopes (
    scope_id text NOT NULL,
    scope_type text NOT NULL,
    subject_id text NOT NULL,
    from_at timestamp with time zone NOT NULL,
    to_at timestamp with time zone NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT inspection_scopes_check CHECK ((to_at > from_at)),
    CONSTRAINT inspection_scopes_scope_type_check CHECK ((scope_type = ANY (ARRAY['analysis-window'::text, 'production-run'::text, 'material-lot'::text])))
);


--
-- Name: knowledge_extraction_jobs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.knowledge_extraction_jobs (
    source_id uuid NOT NULL,
    requested_by text NOT NULL,
    status text NOT NULL,
    attempt_count integer DEFAULT 0 NOT NULL,
    available_at timestamp with time zone DEFAULT now() NOT NULL,
    lease_id uuid,
    leased_at timestamp with time zone,
    last_error text,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT knowledge_extraction_jobs_attempt_count_check CHECK ((attempt_count >= 0)),
    CONSTRAINT knowledge_extraction_jobs_check CHECK (((status = 'running'::text) = ((lease_id IS NOT NULL) AND (leased_at IS NOT NULL)))),
    CONSTRAINT knowledge_extraction_jobs_status_check CHECK ((status = ANY (ARRAY['queued'::text, 'running'::text, 'completed'::text, 'failed'::text, 'dead-letter'::text])))
);


--
-- Name: knowledge_fragment_values; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.knowledge_fragment_values (
    fragment_id uuid NOT NULL,
    value_code text NOT NULL,
    value_text text NOT NULL
);


--
-- Name: knowledge_fragments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.knowledge_fragments (
    record_id uuid NOT NULL,
    source_id uuid NOT NULL,
    human_reviewed boolean NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    category text NOT NULL,
    page_or_sheet text,
    region text,
    content text NOT NULL,
    created_by text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    reviewed_by text,
    reviewed_at timestamp with time zone,
    extraction_method text NOT NULL,
    extractor_version text NOT NULL,
    extraction_confidence double precision,
    location_kind text,
    page_number integer,
    sheet_name text,
    cell_range text,
    citation_region text,
    content_hash text
);


--
-- Name: knowledge_source_context; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.knowledge_source_context (
    source_id uuid NOT NULL,
    dimension_code text NOT NULL,
    dimension_value text NOT NULL
);


--
-- Name: knowledge_sources; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.knowledge_sources (
    source_id uuid NOT NULL,
    status text NOT NULL,
    storage_ref text NOT NULL,
    sha256 text NOT NULL,
    file_name text NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    project_id uuid NOT NULL,
    title text NOT NULL,
    source_kind text NOT NULL,
    media_type text NOT NULL,
    size_bytes bigint NOT NULL,
    extraction_status text NOT NULL,
    extraction_error text,
    extractor_version text,
    uploaded_by text NOT NULL,
    uploaded_at timestamp with time zone NOT NULL,
    reviewed_by text,
    reviewed_at timestamp with time zone,
    CONSTRAINT knowledge_sources_status_check CHECK ((status = ANY (ARRAY['uploaded'::text, 'indexed'::text, 'reviewed'::text, 'retired'::text])))
);


--
-- Name: mechanism_claim_applicability; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_claim_applicability (
    claim_id uuid NOT NULL,
    claim_version integer NOT NULL,
    dimension_code text NOT NULL,
    dimension_value text NOT NULL
);


--
-- Name: mechanism_claim_conflicts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_claim_conflicts (
    conflict_id uuid NOT NULL,
    project_id uuid NOT NULL,
    left_claim_id uuid NOT NULL,
    left_claim_version integer NOT NULL,
    right_claim_id uuid NOT NULL,
    right_claim_version integer NOT NULL,
    conflict_kind text NOT NULL,
    rationale text NOT NULL,
    status text NOT NULL,
    created_by text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    resolved_by text,
    resolved_at timestamp with time zone,
    resolution text,
    CONSTRAINT mechanism_claim_conflicts_check CHECK ((left_claim_id <> right_claim_id)),
    CONSTRAINT mechanism_claim_conflicts_status_check CHECK ((status = ANY (ARRAY['open'::text, 'resolved'::text]))),
    CONSTRAINT mechanism_conflict_resolution_check CHECK ((((status = 'open'::text) AND (resolved_by IS NULL) AND (resolved_at IS NULL) AND (resolution IS NULL)) OR ((status = 'resolved'::text) AND (resolved_by IS NOT NULL) AND (resolved_at IS NOT NULL) AND (resolution IS NOT NULL))))
);


--
-- Name: mechanism_claim_constraints; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_claim_constraints (
    constraint_id uuid NOT NULL,
    claim_id uuid NOT NULL,
    claim_version integer NOT NULL,
    variable_code text NOT NULL,
    constraint_kind text NOT NULL,
    minimum double precision,
    maximum double precision,
    unit text NOT NULL,
    severity text NOT NULL,
    CONSTRAINT mechanism_claim_constraints_check CHECK (((minimum IS NOT NULL) OR (maximum IS NOT NULL))),
    CONSTRAINT mechanism_claim_constraints_check1 CHECK (((minimum IS NULL) OR (maximum IS NULL) OR (minimum <= maximum))),
    CONSTRAINT mechanism_claim_constraints_kind_check CHECK ((constraint_kind = ANY (ARRAY['range'::text, 'safe-range'::text, 'preferred-range'::text]))),
    CONSTRAINT mechanism_claim_constraints_severity_check CHECK ((severity = ANY (ARRAY['hard'::text, 'soft'::text])))
);


--
-- Name: mechanism_claim_evidence; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_claim_evidence (
    evidence_link_id uuid NOT NULL,
    claim_id uuid NOT NULL,
    claim_version integer NOT NULL,
    evidence_kind text NOT NULL,
    reference_id text NOT NULL,
    polarity text NOT NULL,
    content_hash text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT mechanism_claim_evidence_hash_check CHECK ((content_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT mechanism_claim_evidence_polarity_check CHECK ((polarity = ANY (ARRAY['supporting'::text, 'opposing'::text])))
);


--
-- Name: mechanism_claim_lifecycle_decisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_claim_lifecycle_decisions (
    decision_id uuid NOT NULL,
    claim_id uuid NOT NULL,
    claim_version integer NOT NULL,
    from_status text NOT NULL,
    to_status text NOT NULL,
    evidence_kind text,
    reference_id text,
    content_hash text,
    comment text,
    decided_by text NOT NULL,
    decided_at timestamp with time zone NOT NULL,
    validation_hypothesis_id uuid,
    evaluation_outcome text,
    evaluation_summary text,
    CONSTRAINT mechanism_claim_lifecycle_decisions_check CHECK (((evidence_kind IS NULL) = (reference_id IS NULL))),
    CONSTRAINT mechanism_claim_lifecycle_decisions_check1 CHECK (((reference_id IS NULL) = (content_hash IS NULL))),
    CONSTRAINT mechanism_claim_lifecycle_decisions_content_hash_check CHECK (((content_hash IS NULL) OR (content_hash ~ '^[0-9a-f]{64}$'::text))),
    CONSTRAINT mechanism_claim_lifecycle_decisions_from_status_check CHECK ((from_status = ANY (ARRAY['reviewed'::text, 'supported'::text, 'validated'::text, 'active'::text]))),
    CONSTRAINT mechanism_claim_lifecycle_decisions_to_status_check CHECK ((to_status = ANY (ARRAY['supported'::text, 'validated'::text, 'active'::text, 'falsified'::text, 'retired'::text]))),
    CONSTRAINT mechanism_claim_lifecycle_transition_check CHECK ((((from_status = 'reviewed'::text) AND (to_status = 'supported'::text)) OR ((from_status = 'supported'::text) AND (to_status = 'validated'::text)) OR ((from_status = 'validated'::text) AND (to_status = 'active'::text)) OR ((from_status = ANY (ARRAY['reviewed'::text, 'supported'::text, 'validated'::text, 'active'::text])) AND (to_status = 'falsified'::text)) OR ((from_status = 'active'::text) AND (to_status = 'retired'::text))))
);


--
-- Name: mechanism_claim_reviews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_claim_reviews (
    review_id uuid NOT NULL,
    claim_id uuid NOT NULL,
    claim_version integer NOT NULL,
    decision text NOT NULL,
    reviewer_id text NOT NULL,
    comment text,
    reviewed_at timestamp with time zone NOT NULL,
    CONSTRAINT mechanism_claim_reviews_decision_check CHECK ((decision = ANY (ARRAY['approve'::text, 'reject'::text])))
);


--
-- Name: mechanism_claim_variables; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_claim_variables (
    claim_id uuid NOT NULL,
    claim_version integer NOT NULL,
    variable_code text NOT NULL,
    variable_role text NOT NULL,
    direction text,
    delay_ms bigint,
    unit text NOT NULL,
    CONSTRAINT mechanism_claim_variables_delay_check CHECK (((delay_ms IS NULL) OR (delay_ms >= 0))),
    CONSTRAINT mechanism_claim_variables_direction_check CHECK (((direction IS NULL) OR (direction = ANY (ARRAY['increase'::text, 'decrease'::text, 'nonlinear'::text])))),
    CONSTRAINT mechanism_claim_variables_role_check CHECK ((variable_role = ANY (ARRAY['cause'::text, 'mediator'::text, 'outcome'::text, 'moderator'::text])))
);


--
-- Name: mechanism_claim_versions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_claim_versions (
    claim_id uuid NOT NULL,
    version integer NOT NULL,
    name text NOT NULL,
    mechanism_type text NOT NULL,
    statement text NOT NULL,
    expected_signature text,
    falsification_condition text NOT NULL,
    evidence_level text NOT NULL,
    created_by text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    reviewed_by text,
    reviewed_at timestamp with time zone,
    content_hash text NOT NULL,
    CONSTRAINT mechanism_claim_versions_hash_check CHECK ((content_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT mechanism_claim_versions_mechanism_type_check CHECK ((mechanism_type = ANY (ARRAY['qualitative'::text, 'monotonic'::text, 'threshold'::text, 'interaction'::text, 'temporal'::text, 'constraint'::text, 'failure-mode'::text, 'executable-model'::text])))
);


--
-- Name: mechanism_claims; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_claims (
    claim_id uuid NOT NULL,
    project_id uuid NOT NULL,
    current_version integer NOT NULL,
    status text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT mechanism_claims_status_check CHECK ((status = ANY (ARRAY['draft'::text, 'reviewed'::text, 'supported'::text, 'validated'::text, 'active'::text, 'rejected'::text, 'falsified'::text, 'retired'::text])))
);


--
-- Name: mechanism_fusion_definitions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_fusion_definitions (
    fusion_id text NOT NULL,
    version integer NOT NULL,
    status text NOT NULL,
    mode text NOT NULL,
    mechanism_model_id text NOT NULL,
    mechanism_model_version integer NOT NULL,
    content_hash text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT mechanism_fusion_definitions_mode_check CHECK ((mode = ANY (ARRAY['calibration'::text, 'post-processing'::text, 'mechanism-as-feature'::text, 'ensemble'::text]))),
    CONSTRAINT mechanism_fusion_definitions_status_check CHECK ((status = ANY (ARRAY['draft'::text, 'validated'::text, 'active'::text, 'retired'::text]))),
    CONSTRAINT mechanism_fusion_definitions_version_check CHECK ((version > 0))
);


--
-- Name: mechanism_model_versions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mechanism_model_versions (
    model_id text NOT NULL,
    version integer NOT NULL,
    status text NOT NULL,
    content_hash text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT mechanism_model_versions_status_check CHECK ((status = ANY (ARRAY['draft'::text, 'validated'::text, 'active'::text, 'retired'::text]))),
    CONSTRAINT mechanism_model_versions_version_check CHECK ((version > 0))
);


--
-- Name: model_drift_readings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.model_drift_readings (
    reading_id uuid NOT NULL,
    model_id text NOT NULL,
    model_version integer NOT NULL,
    value double precision NOT NULL,
    stop_threshold double precision NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL
);


--
-- Name: model_evaluations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.model_evaluations (
    evaluation_id uuid NOT NULL,
    model_id text NOT NULL,
    model_version integer NOT NULL,
    passed boolean NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL
);


--
-- Name: operation_context_snapshots; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.operation_context_snapshots (
    execution_id text NOT NULL,
    subject_type text NOT NULL,
    subject_id text NOT NULL,
    started_event_type text NOT NULL,
    captured_at timestamp with time zone NOT NULL,
    context jsonb NOT NULL
);


--
-- Name: phase_definitions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.phase_definitions (
    code text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


--
-- Name: phase_mappings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.phase_mappings (
    mapping_id text NOT NULL,
    process_specification_id text NOT NULL,
    process_specification_version text,
    process_template text,
    process_step text NOT NULL,
    phase_code text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


--
-- Name: platform_edges; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.platform_edges (
    edge_id text NOT NULL,
    host_base_url text,
    hostname text,
    version text,
    last_seen_at timestamp with time zone NOT NULL,
    last_error text,
    acquisition_status jsonb,
    delivery_status jsonb
);


--
-- Name: problem_cases; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.problem_cases (
    case_id uuid NOT NULL,
    title text NOT NULL,
    description text DEFAULT ''::text NOT NULL,
    status text DEFAULT 'open'::text NOT NULL,
    subject_type text,
    subject_id text,
    context_filter jsonb DEFAULT '{}'::jsonb NOT NULL,
    comparison_key text,
    window_from timestamp with time zone,
    window_to timestamp with time zone,
    target_metric text DEFAULT ''::text NOT NULL,
    current_level text DEFAULT 'L0-pending'::text NOT NULL,
    feature_set_ratified boolean DEFAULT false NOT NULL,
    ratified_by text,
    ratified_at timestamp with time zone,
    owner text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_problem_cases_status CHECK ((status = ANY (ARRAY['open'::text, 'resolved'::text, 'archived'::text])))
);


--
-- Name: process_analysis_plans; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.process_analysis_plans (
    plan_id text NOT NULL,
    version integer NOT NULL,
    data_model_id text NOT NULL,
    data_model_version integer NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT process_analysis_plans_data_model_version_check CHECK ((data_model_version > 0)),
    CONSTRAINT process_analysis_plans_version_check CHECK ((version > 0))
);


--
-- Name: process_data_models; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.process_data_models (
    model_id text NOT NULL,
    version integer NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT process_data_models_version_check CHECK ((version > 0))
);


--
-- Name: process_model_versions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.process_model_versions (
    model_id text NOT NULL,
    version integer NOT NULL,
    status text NOT NULL,
    dataset_id text NOT NULL,
    dataset_version integer NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT process_model_versions_status_check CHECK ((status = ANY (ARRAY['draft'::text, 'validated'::text, 'active'::text, 'suspended'::text, 'retired'::text]))),
    CONSTRAINT process_model_versions_version_check CHECK ((version > 0))
);


--
-- Name: process_research_audit; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.process_research_audit (
    entry_id uuid NOT NULL,
    project_id uuid NOT NULL,
    resource_type text NOT NULL,
    resource_id text NOT NULL,
    action text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL
);


--
-- Name: process_research_projects; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.process_research_projects (
    project_id uuid NOT NULL,
    code text NOT NULL,
    status text NOT NULL,
    revision integer NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT process_research_projects_revision_check CHECK ((revision > 0)),
    CONSTRAINT process_research_projects_status_check CHECK ((status = ANY (ARRAY['draft'::text, 'active'::text, 'validating'::text, 'completed'::text, 'archived'::text])))
);


--
-- Name: process_sample_frames; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.process_sample_frames (
    occurred_at timestamp with time zone NOT NULL,
    frame_id bigint NOT NULL,
    event_id text NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    ingested_at timestamp with time zone NOT NULL,
    site_id text NOT NULL,
    edge_id text NOT NULL,
    source text NOT NULL,
    subject_type text NOT NULL,
    subject_id text NOT NULL,
    execution_id text,
    phase_code text,
    data_model_id text NOT NULL,
    data_model_version integer NOT NULL,
    CONSTRAINT process_sample_frames_site_id_check CHECK ((site_id ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$'::text))
);


--
-- Name: process_sample_values; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.process_sample_values (
    occurred_at timestamp with time zone NOT NULL,
    frame_id bigint NOT NULL,
    point_key bigint NOT NULL,
    quality_code smallint NOT NULL,
    numeric_value double precision,
    integer_value bigint,
    boolean_value boolean,
    text_value text,
    CONSTRAINT ck_process_sample_values_one_value CHECK ((num_nonnulls(numeric_value, integer_value, boolean_value, text_value) = 1)),
    CONSTRAINT ck_process_sample_values_quality CHECK (((quality_code >= 0) AND (quality_code <= 2)))
);


--
-- Name: process_specification_versions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.process_specification_versions (
    process_specification_id text NOT NULL,
    version integer NOT NULL,
    data_model_id text NOT NULL,
    data_model_version integer NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT process_specification_versions_data_model_version_check CHECK ((data_model_version > 0)),
    CONSTRAINT process_specification_versions_version_check CHECK ((version > 0))
);


--
-- Name: production_contexts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.production_contexts (
    context_id uuid NOT NULL,
    equipment_id text NOT NULL,
    tooling_installation_id uuid NOT NULL,
    valid_from timestamp with time zone NOT NULL,
    valid_to timestamp with time zone,
    source text NOT NULL,
    command_id text,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT production_contexts_check CHECK (((valid_to IS NULL) OR (valid_to > valid_from)))
);


--
-- Name: production_events_ingest_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.production_events_ingest_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: production_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.production_events (
    ingest_id bigint DEFAULT nextval('public.production_events_ingest_id_seq'::regclass) NOT NULL,
    event_id text NOT NULL,
    site_id text NOT NULL,
    edge_id text NOT NULL,
    seq bigint NOT NULL,
    schema_version integer NOT NULL,
    event_type text NOT NULL,
    type_version integer NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    ingested_at timestamp with time zone DEFAULT now() NOT NULL,
    source text NOT NULL,
    subject_type text NOT NULL,
    subject_id text NOT NULL,
    execution_id text,
    configuration_kind text,
    configuration_id text,
    configuration_version integer,
    quality_flags jsonb DEFAULT '[]'::jsonb NOT NULL,
    payload_hash text NOT NULL,
    context jsonb DEFAULT '{}'::jsonb NOT NULL,
    data jsonb DEFAULT '{}'::jsonb NOT NULL,
    CONSTRAINT production_events_configuration_check CHECK ((((configuration_kind IS NULL) AND (configuration_id IS NULL) AND (configuration_version IS NULL)) OR ((configuration_kind IS NOT NULL) AND (configuration_id IS NOT NULL) AND (configuration_version > 0)))),
    CONSTRAINT production_events_payload_hash_check CHECK ((payload_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT production_events_quality_flags_check CHECK ((jsonb_typeof(quality_flags) = 'array'::text)),
    CONSTRAINT production_events_schema_version_check CHECK ((schema_version = 1)),
    CONSTRAINT production_events_site_id_check CHECK ((site_id ~ '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$'::text))
);


--
-- Name: recommendation_knowledge_usage; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.recommendation_knowledge_usage (
    recommendation_id uuid NOT NULL,
    claim_id uuid NOT NULL,
    claim_version integer NOT NULL,
    usage_type text NOT NULL,
    content_hash text NOT NULL,
    CONSTRAINT recommendation_knowledge_usage_hash_check CHECK ((content_hash ~ '^[0-9a-f]{64}$'::text))
);


--
-- Name: research_asset_audit; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_asset_audit (
    entry_id uuid NOT NULL,
    resource_type text NOT NULL,
    resource_id text NOT NULL,
    action text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL
);


--
-- Name: research_evidence; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_evidence (
    evidence_id uuid NOT NULL,
    project_id uuid NOT NULL,
    resource_type text NOT NULL,
    resource_id text NOT NULL,
    kind text NOT NULL,
    reference_id text NOT NULL,
    content_hash text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT research_evidence_content_hash_check CHECK ((content_hash ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT research_evidence_kind_check CHECK ((kind = ANY (ARRAY['dataset-snapshot'::text, 'experiment-result'::text, 'analysis-run'::text, 'execution-comparison'::text, 'mechanism-model'::text, 'knowledge-source'::text, 'operating-region'::text, 'transfer-assessment'::text])))
);


--
-- Name: research_experiment_results; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_experiment_results (
    result_id uuid NOT NULL,
    project_id uuid NOT NULL,
    experiment_id uuid NOT NULL,
    analysis_run_id uuid NOT NULL,
    analysis_hash text NOT NULL,
    safety_passed boolean NOT NULL,
    payload jsonb NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    CONSTRAINT research_experiment_results_analysis_hash_check CHECK ((analysis_hash ~ '^[0-9a-f]{64}$'::text))
);


--
-- Name: research_experiment_runs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_experiment_runs (
    experiment_id uuid NOT NULL,
    execution_key text NOT NULL,
    sequence integer NOT NULL,
    payload jsonb NOT NULL,
    CONSTRAINT research_experiment_runs_sequence_check CHECK ((sequence > 0))
);


--
-- Name: research_experiments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_experiments (
    experiment_id uuid NOT NULL,
    project_id uuid NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    revision integer DEFAULT 1 NOT NULL,
    CONSTRAINT research_experiments_revision_positive CHECK ((revision > 0)),
    CONSTRAINT research_experiments_status_check CHECK ((status = ANY (ARRAY['planned'::text, 'approved'::text, 'running'::text, 'completed'::text, 'cancelled'::text])))
);


--
-- Name: research_historical_replay_reports; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_historical_replay_reports (
    report_id uuid NOT NULL,
    project_id uuid NOT NULL,
    status text NOT NULL,
    dataset_snapshot_hash text NOT NULL,
    report_hash text NOT NULL,
    payload jsonb NOT NULL,
    generated_at timestamp with time zone NOT NULL,
    reviewed_at timestamp with time zone,
    CONSTRAINT research_historical_replay_reports_dataset_snapshot_hash_check CHECK ((dataset_snapshot_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT research_historical_replay_reports_report_hash_check CHECK ((report_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT research_historical_replay_reports_status_check CHECK ((status = ANY (ARRAY['generated'::text, 'reviewed'::text])))
);


--
-- Name: research_hypotheses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_hypotheses (
    hypothesis_id uuid NOT NULL,
    project_id uuid NOT NULL,
    status text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    statement text NOT NULL,
    rationale text NOT NULL,
    validation_outcome_code text,
    expected_effect_direction text,
    minimum_effect double precision,
    applicability text,
    confidence double precision NOT NULL,
    created_by text NOT NULL,
    CONSTRAINT research_hypotheses_confidence_check CHECK (((confidence >= (0)::double precision) AND (confidence <= (1)::double precision))),
    CONSTRAINT research_hypotheses_status_check CHECK ((status = ANY (ARRAY['proposed'::text, 'selected'::text, 'supported'::text, 'validated'::text, 'rejected'::text, 'inconclusive'::text])))
);


--
-- Name: research_hypothesis_causal_links; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_hypothesis_causal_links (
    hypothesis_id uuid NOT NULL,
    sequence integer NOT NULL,
    from_variable_code text NOT NULL,
    to_variable_code text NOT NULL,
    mechanism text NOT NULL,
    direction text,
    CONSTRAINT research_hypothesis_causal_links_check CHECK ((from_variable_code <> to_variable_code))
);


--
-- Name: research_hypothesis_confounders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_hypothesis_confounders (
    hypothesis_id uuid NOT NULL,
    sequence integer NOT NULL,
    description text NOT NULL
);


--
-- Name: research_hypothesis_evidence; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_hypothesis_evidence (
    hypothesis_id uuid NOT NULL,
    evidence_id uuid NOT NULL,
    evidence_role text NOT NULL,
    project_id uuid NOT NULL,
    kind text NOT NULL,
    reference_id text NOT NULL,
    summary text NOT NULL,
    content_hash text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT research_hypothesis_evidence_evidence_role_check CHECK ((evidence_role = ANY (ARRAY['supporting'::text, 'opposing'::text, 'validation'::text])))
);


--
-- Name: research_hypothesis_failure_conditions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_hypothesis_failure_conditions (
    failure_condition_id uuid NOT NULL,
    hypothesis_id uuid NOT NULL,
    sequence integer NOT NULL,
    condition text NOT NULL,
    observable_signal text NOT NULL,
    required_response text NOT NULL
);


--
-- Name: research_hypothesis_falsification_conditions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_hypothesis_falsification_conditions (
    hypothesis_id uuid NOT NULL,
    sequence integer NOT NULL,
    condition text NOT NULL
);


--
-- Name: research_hypothesis_interaction_variables; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_hypothesis_interaction_variables (
    interaction_id uuid NOT NULL,
    sequence integer NOT NULL,
    variable_code text NOT NULL
);


--
-- Name: research_hypothesis_interactions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_hypothesis_interactions (
    interaction_id uuid NOT NULL,
    hypothesis_id uuid NOT NULL,
    sequence integer NOT NULL,
    description text NOT NULL
);


--
-- Name: research_hypothesis_temporal_features; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_hypothesis_temporal_features (
    hypothesis_id uuid NOT NULL,
    sequence integer NOT NULL,
    variable_code text NOT NULL,
    feature_code text NOT NULL,
    phase_code text,
    delay_ms bigint,
    window_ms bigint,
    CONSTRAINT research_hypothesis_temporal_features_delay_ms_check CHECK (((delay_ms IS NULL) OR (delay_ms >= 0))),
    CONSTRAINT research_hypothesis_temporal_features_window_ms_check CHECK (((window_ms IS NULL) OR (window_ms > 0)))
);


--
-- Name: research_hypothesis_variables; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_hypothesis_variables (
    hypothesis_id uuid NOT NULL,
    sequence integer NOT NULL,
    variable_code text NOT NULL
);


--
-- Name: research_knowledge_claims; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_knowledge_claims (
    claim_id uuid NOT NULL,
    project_id uuid NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT research_knowledge_claims_status_check CHECK ((status = ANY (ARRAY['draft'::text, 'reviewed'::text, 'published'::text, 'retired'::text])))
);


--
-- Name: research_operating_region_results; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_operating_region_results (
    operating_region_id uuid NOT NULL,
    result_id uuid NOT NULL
);


--
-- Name: research_operating_regions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_operating_regions (
    operating_region_id uuid NOT NULL,
    project_id uuid NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT research_operating_regions_status_check CHECK ((status = ANY (ARRAY['candidate'::text, 'validated'::text, 'superseded'::text])))
);


--
-- Name: research_project_members; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_project_members (
    project_id uuid NOT NULL,
    user_id text NOT NULL
);


--
-- Name: research_rollback_drills; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_rollback_drills (
    drill_id uuid NOT NULL,
    project_id uuid NOT NULL,
    status text NOT NULL,
    passed boolean NOT NULL,
    record_hash text NOT NULL,
    payload jsonb NOT NULL,
    conducted_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL,
    reviewed_at timestamp with time zone,
    CONSTRAINT research_rollback_drills_record_hash_check CHECK ((record_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT research_rollback_drills_status_check CHECK ((status = ANY (ARRAY['recorded'::text, 'reviewed'::text])))
);


--
-- Name: research_shadow_recommendations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_shadow_recommendations (
    recommendation_id uuid NOT NULL,
    project_id uuid NOT NULL,
    experiment_id uuid NOT NULL,
    suggestion_execution_key text NOT NULL,
    actual_execution_key text NOT NULL,
    decision text NOT NULL,
    payload jsonb NOT NULL,
    decided_at timestamp with time zone NOT NULL,
    CONSTRAINT research_shadow_recommendations_decision_check CHECK ((decision = ANY (ARRAY['accepted'::text, 'modified'::text, 'rejected'::text])))
);


--
-- Name: research_transfer_assessments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_transfer_assessments (
    assessment_id uuid NOT NULL,
    project_id uuid NOT NULL,
    source_project_id uuid NOT NULL,
    source_operating_region_id uuid NOT NULL,
    status text NOT NULL,
    outcome text NOT NULL,
    record_hash text NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    reviewed_at timestamp with time zone,
    CONSTRAINT research_transfer_assessments_outcome_check CHECK ((outcome = ANY (ARRAY['beneficial'::text, 'neutral'::text, 'negative-transfer'::text, 'insufficient-evidence'::text]))),
    CONSTRAINT research_transfer_assessments_record_hash_check CHECK ((record_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT research_transfer_assessments_status_check CHECK ((status = ANY (ARRAY['recorded'::text, 'reviewed'::text])))
);


--
-- Name: research_validation_preregistrations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.research_validation_preregistrations (
    preregistration_id uuid NOT NULL,
    project_id uuid NOT NULL,
    version integer NOT NULL,
    project_revision integer NOT NULL,
    status text NOT NULL,
    project_snapshot_hash text NOT NULL,
    content_hash text NOT NULL,
    payload jsonb NOT NULL,
    frozen_at timestamp with time zone NOT NULL,
    reviewed_at timestamp with time zone,
    CONSTRAINT research_validation_preregistration_project_snapshot_hash_check CHECK ((project_snapshot_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT research_validation_preregistrations_content_hash_check CHECK ((content_hash ~ '^[a-f0-9]{64}$'::text)),
    CONSTRAINT research_validation_preregistrations_project_revision_check CHECK ((project_revision > 0)),
    CONSTRAINT research_validation_preregistrations_status_check CHECK ((status = ANY (ARRAY['frozen'::text, 'reviewed'::text]))),
    CONSTRAINT research_validation_preregistrations_version_check CHECK ((version > 0))
);


--
-- Name: scenario_packages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.scenario_packages (
    package_id text NOT NULL,
    version integer NOT NULL,
    data_model_id text NOT NULL,
    data_model_version integer NOT NULL,
    status text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT scenario_packages_data_model_version_check CHECK ((data_model_version > 0)),
    CONSTRAINT scenario_packages_version_check CHECK ((version > 0))
);


--
-- Name: signal_definitions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.signal_definitions (
    data_model_id text NOT NULL,
    data_model_version integer NOT NULL,
    signal_code text NOT NULL,
    source_field text NOT NULL,
    data_type text NOT NULL,
    unit text,
    category text NOT NULL,
    definition_hash text NOT NULL,
    first_seen_at timestamp with time zone NOT NULL,
    last_seen_at timestamp with time zone NOT NULL
);


--
-- Name: tooling_assemblies; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tooling_assemblies (
    tooling_assembly_id text NOT NULL,
    tooling_type_code text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


--
-- Name: tooling_assembly_revisions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tooling_assembly_revisions (
    assembly_revision_id uuid NOT NULL,
    tooling_assembly_id text NOT NULL,
    revision integer NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT tooling_assembly_revisions_revision_check CHECK ((revision > 0))
);


--
-- Name: tooling_component_types; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tooling_component_types (
    component_type_code text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


--
-- Name: tooling_components; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tooling_components (
    component_id text NOT NULL,
    component_type_code text NOT NULL,
    serial_no text NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL
);


--
-- Name: tooling_installations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tooling_installations (
    installation_id uuid NOT NULL,
    equipment_id text NOT NULL,
    assembly_revision_id uuid NOT NULL,
    installed_at timestamp with time zone NOT NULL,
    removed_at timestamp with time zone,
    source text NOT NULL,
    command_id text,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT tooling_installations_check CHECK (((removed_at IS NULL) OR (removed_at > installed_at)))
);


--
-- Name: tooling_types; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tooling_types (
    tooling_type_code text NOT NULL,
    version integer NOT NULL,
    payload jsonb NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT tooling_types_version_check CHECK ((version > 0))
);


--
-- Name: tooling_usage_counters; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.tooling_usage_counters (
    tooling_installation_id uuid NOT NULL,
    started_run_count bigint NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT tooling_usage_counters_started_run_count_check CHECK ((started_run_count >= 0))
);


--
-- Name: training_dataset_versions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.training_dataset_versions (
    dataset_id text NOT NULL,
    version integer NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT training_dataset_versions_version_check CHECK ((version > 0))
);


--
-- Name: user_sessions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_sessions (
    token_hash text NOT NULL,
    user_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    last_seen_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    user_id uuid NOT NULL,
    username text NOT NULL,
    username_lower text NOT NULL,
    display_name text DEFAULT ''::text NOT NULL,
    password_hash text NOT NULL,
    roles text[] DEFAULT '{}'::text[] NOT NULL,
    disabled boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: inspection_audit_log audit_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inspection_audit_log ALTER COLUMN audit_id SET DEFAULT nextval('public.inspection_audit_log_audit_id_seq'::regclass);


--
-- Name: agent_runs agent_runs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.agent_runs
    ADD CONSTRAINT agent_runs_pkey PRIMARY KEY (run_id);


--
-- Name: agent_stream_events agent_stream_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.agent_stream_events
    ADD CONSTRAINT agent_stream_events_pkey PRIMARY KEY (sequence);


--
-- Name: case_level_evaluations case_level_evaluations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_level_evaluations
    ADD CONSTRAINT case_level_evaluations_pkey PRIMARY KEY (evaluation_id);


--
-- Name: collection_points collection_points_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.collection_points
    ADD CONSTRAINT collection_points_pkey PRIMARY KEY (collection_point_id);


--
-- Name: data_object_operation_keys data_object_operation_keys_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.data_object_operation_keys
    ADD CONSTRAINT data_object_operation_keys_pkey PRIMARY KEY (site_id, subject_type, subject_id, execution_id);


--
-- Name: data_object_summaries data_object_summaries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.data_object_summaries
    ADD CONSTRAINT data_object_summaries_pkey PRIMARY KEY (site_id, subject_type, subject_id);


--
-- Name: data_source_instances data_source_instances_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.data_source_instances
    ADD CONSTRAINT data_source_instances_pkey PRIMARY KEY (data_source_id, version);


--
-- Name: edge_runtime_status_history edge_runtime_status_history_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.edge_runtime_status_history
    ADD CONSTRAINT edge_runtime_status_history_pkey PRIMARY KEY (edge_id, recorded_at);


--
-- Name: event_ingest_keys event_ingest_keys_site_id_edge_id_seq_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.event_ingest_keys
    ADD CONSTRAINT event_ingest_keys_site_id_edge_id_seq_key UNIQUE (site_id, edge_id, seq);


--
-- Name: event_ingest_keys event_ingest_keys_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.event_ingest_keys
    ADD CONSTRAINT event_ingest_keys_pkey PRIMARY KEY (event_id);


--
-- Name: execution_analysis_backfill_jobs execution_analysis_backfill_jobs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.execution_analysis_backfill_jobs
    ADD CONSTRAINT execution_analysis_backfill_jobs_pkey PRIMARY KEY (job_id);


--
-- Name: execution_analysis_materializations execution_analysis_materializations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.execution_analysis_materializations
    ADD CONSTRAINT execution_analysis_materializations_pkey PRIMARY KEY (execution_id, algorithm_version, data_model_id, data_model_version, analysis_plan_id, analysis_plan_version);


--
-- Name: execution_analysis_recompute_jobs execution_analysis_recompute_jobs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.execution_analysis_recompute_jobs
    ADD CONSTRAINT execution_analysis_recompute_jobs_pkey PRIMARY KEY (execution_id);


--
-- Name: execution_features execution_features_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.execution_features
    ADD CONSTRAINT execution_features_pkey PRIMARY KEY (execution_id, algorithm_version, data_model_id, data_model_version, analysis_plan_id, analysis_plan_version, signal_code, phase_code, phase_order, feature_code);


--
-- Name: execution_phases execution_phases_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.execution_phases
    ADD CONSTRAINT execution_phases_pkey PRIMARY KEY (execution_id, algorithm_version, data_model_id, data_model_version, analysis_plan_id, analysis_plan_version, phase_order);


--
-- Name: feature_definitions feature_definitions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.feature_definitions
    ADD CONSTRAINT feature_definitions_pkey PRIMARY KEY (code);


--
-- Name: golden_question_cases golden_question_cases_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.golden_question_cases
    ADD CONSTRAINT golden_question_cases_pkey PRIMARY KEY (case_id, version);


--
-- Name: golden_question_evaluations golden_question_evaluations_case_id_case_version_agent_run__key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.golden_question_evaluations
    ADD CONSTRAINT golden_question_evaluations_case_id_case_version_agent_run__key UNIQUE (case_id, case_version, agent_run_id);


--
-- Name: golden_question_evaluations golden_question_evaluations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.golden_question_evaluations
    ADD CONSTRAINT golden_question_evaluations_pkey PRIMARY KEY (evaluation_id);


--
-- Name: ingestion_task_bindings ingestion_task_bindings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ingestion_task_bindings
    ADD CONSTRAINT ingestion_task_bindings_pkey PRIMARY KEY (task_id, version);


--
-- Name: ingestion_task_templates ingestion_task_templates_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ingestion_task_templates
    ADD CONSTRAINT ingestion_task_templates_pkey PRIMARY KEY (template_id, version);


--
-- Name: ingestion_tasks ingestion_tasks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ingestion_tasks
    ADD CONSTRAINT ingestion_tasks_pkey PRIMARY KEY (task_id, version);


--
-- Name: inspection_attachments inspection_attachments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inspection_attachments
    ADD CONSTRAINT inspection_attachments_pkey PRIMARY KEY (attachment_id);


--
-- Name: inspection_attachments inspection_attachments_sha256_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inspection_attachments
    ADD CONSTRAINT inspection_attachments_sha256_key UNIQUE (sha256);


--
-- Name: inspection_audit_log inspection_audit_log_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inspection_audit_log
    ADD CONSTRAINT inspection_audit_log_pkey PRIMARY KEY (audit_id);


--
-- Name: inspection_definitions inspection_definitions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inspection_definitions
    ADD CONSTRAINT inspection_definitions_pkey PRIMARY KEY (code, version);


--
-- Name: inspection_plans inspection_plans_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inspection_plans
    ADD CONSTRAINT inspection_plans_pkey PRIMARY KEY (plan_id, version);


--
-- Name: inspection_records inspection_records_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inspection_records
    ADD CONSTRAINT inspection_records_pkey PRIMARY KEY (record_id);


--
-- Name: inspection_reviews inspection_reviews_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inspection_reviews
    ADD CONSTRAINT inspection_reviews_pkey PRIMARY KEY (review_id);


--
-- Name: inspection_scopes inspection_scopes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inspection_scopes
    ADD CONSTRAINT inspection_scopes_pkey PRIMARY KEY (scope_id);


--
-- Name: knowledge_extraction_jobs knowledge_extraction_jobs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_extraction_jobs
    ADD CONSTRAINT knowledge_extraction_jobs_pkey PRIMARY KEY (source_id);


--
-- Name: knowledge_fragment_values knowledge_fragment_values_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_fragment_values
    ADD CONSTRAINT knowledge_fragment_values_pkey PRIMARY KEY (fragment_id, value_code);


--
-- Name: knowledge_source_context knowledge_source_context_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_source_context
    ADD CONSTRAINT knowledge_source_context_pkey PRIMARY KEY (source_id, dimension_code);


--
-- Name: mechanism_claim_applicability mechanism_claim_applicability_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_applicability
    ADD CONSTRAINT mechanism_claim_applicability_pkey PRIMARY KEY (claim_id, claim_version, dimension_code, dimension_value);


--
-- Name: mechanism_claim_conflicts mechanism_claim_conflicts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_conflicts
    ADD CONSTRAINT mechanism_claim_conflicts_pkey PRIMARY KEY (conflict_id);


--
-- Name: mechanism_claim_constraints mechanism_claim_constraints_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_constraints
    ADD CONSTRAINT mechanism_claim_constraints_pkey PRIMARY KEY (constraint_id);


--
-- Name: mechanism_claim_evidence mechanism_claim_evidence_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_evidence
    ADD CONSTRAINT mechanism_claim_evidence_pkey PRIMARY KEY (evidence_link_id);


--
-- Name: mechanism_claim_lifecycle_decisions mechanism_claim_lifecycle_decisions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_lifecycle_decisions
    ADD CONSTRAINT mechanism_claim_lifecycle_decisions_pkey PRIMARY KEY (decision_id);


--
-- Name: mechanism_claim_lifecycle_decisions mechanism_claim_lifecycle_evaluation_check; Type: CHECK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE public.mechanism_claim_lifecycle_decisions
    ADD CONSTRAINT mechanism_claim_lifecycle_evaluation_check CHECK ((((to_status = ANY (ARRAY['supported'::text, 'validated'::text])) AND (validation_hypothesis_id IS NOT NULL) AND (evaluation_outcome = 'supports'::text) AND (evaluation_summary IS NOT NULL)) OR ((to_status = 'falsified'::text) AND (validation_hypothesis_id IS NOT NULL) AND (evaluation_outcome = 'falsifies'::text) AND (evaluation_summary IS NOT NULL)) OR ((to_status <> ALL (ARRAY['supported'::text, 'validated'::text, 'falsified'::text])) AND (validation_hypothesis_id IS NULL) AND (evaluation_outcome IS NULL) AND (evaluation_summary IS NULL))));


--
-- Name: mechanism_claim_reviews mechanism_claim_reviews_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_reviews
    ADD CONSTRAINT mechanism_claim_reviews_pkey PRIMARY KEY (review_id);


--
-- Name: mechanism_claim_variables mechanism_claim_variables_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_variables
    ADD CONSTRAINT mechanism_claim_variables_pkey PRIMARY KEY (claim_id, claim_version, variable_code, variable_role);


--
-- Name: mechanism_claim_versions mechanism_claim_versions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_versions
    ADD CONSTRAINT mechanism_claim_versions_pkey PRIMARY KEY (claim_id, version);


--
-- Name: mechanism_claims mechanism_claims_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claims
    ADD CONSTRAINT mechanism_claims_pkey PRIMARY KEY (claim_id);


--
-- Name: mechanism_fusion_definitions mechanism_fusion_definitions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_fusion_definitions
    ADD CONSTRAINT mechanism_fusion_definitions_pkey PRIMARY KEY (fusion_id, version);


--
-- Name: mechanism_model_versions mechanism_model_versions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_model_versions
    ADD CONSTRAINT mechanism_model_versions_pkey PRIMARY KEY (model_id, version);


--
-- Name: model_drift_readings model_drift_readings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.model_drift_readings
    ADD CONSTRAINT model_drift_readings_pkey PRIMARY KEY (reading_id);


--
-- Name: model_evaluations model_evaluations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.model_evaluations
    ADD CONSTRAINT model_evaluations_pkey PRIMARY KEY (evaluation_id);


--
-- Name: operation_context_snapshots operation_context_snapshots_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.operation_context_snapshots
    ADD CONSTRAINT operation_context_snapshots_pkey PRIMARY KEY (execution_id);


--
-- Name: phase_definitions phase_definitions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.phase_definitions
    ADD CONSTRAINT phase_definitions_pkey PRIMARY KEY (code);


--
-- Name: phase_mappings phase_mappings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.phase_mappings
    ADD CONSTRAINT phase_mappings_pkey PRIMARY KEY (mapping_id);


--
-- Name: platform_edges platform_edges_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.platform_edges
    ADD CONSTRAINT platform_edges_pkey PRIMARY KEY (edge_id);


--
-- Name: problem_cases problem_cases_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.problem_cases
    ADD CONSTRAINT problem_cases_pkey PRIMARY KEY (case_id);


--
-- Name: process_analysis_plans process_analysis_plans_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.process_analysis_plans
    ADD CONSTRAINT process_analysis_plans_pkey PRIMARY KEY (plan_id, version);


--
-- Name: process_data_models process_data_models_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.process_data_models
    ADD CONSTRAINT process_data_models_pkey PRIMARY KEY (model_id, version);


--
-- Name: knowledge_fragments knowledge_fragments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_fragments
    ADD CONSTRAINT knowledge_fragments_pkey PRIMARY KEY (record_id);


--
-- Name: knowledge_sources knowledge_sources_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_sources
    ADD CONSTRAINT knowledge_sources_pkey PRIMARY KEY (source_id);


--
-- Name: process_model_versions process_model_versions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.process_model_versions
    ADD CONSTRAINT process_model_versions_pkey PRIMARY KEY (model_id, version);


--
-- Name: process_research_audit process_research_audit_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.process_research_audit
    ADD CONSTRAINT process_research_audit_pkey PRIMARY KEY (entry_id);


--
-- Name: process_research_projects process_research_projects_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.process_research_projects
    ADD CONSTRAINT process_research_projects_code_key UNIQUE (code);


--
-- Name: process_research_projects process_research_projects_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.process_research_projects
    ADD CONSTRAINT process_research_projects_pkey PRIMARY KEY (project_id);


--
-- Name: process_specification_versions process_specification_versions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.process_specification_versions
    ADD CONSTRAINT process_specification_versions_pkey PRIMARY KEY (process_specification_id, version);


--
-- Name: production_contexts production_contexts_command_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.production_contexts
    ADD CONSTRAINT production_contexts_command_id_key UNIQUE (command_id);


--
-- Name: production_contexts production_contexts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.production_contexts
    ADD CONSTRAINT production_contexts_pkey PRIMARY KEY (context_id);


--
-- Name: recommendation_knowledge_usage recommendation_knowledge_usage_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.recommendation_knowledge_usage
    ADD CONSTRAINT recommendation_knowledge_usage_pkey PRIMARY KEY (recommendation_id, claim_id, claim_version, usage_type);


--
-- Name: research_asset_audit research_asset_audit_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_asset_audit
    ADD CONSTRAINT research_asset_audit_pkey PRIMARY KEY (entry_id);


--
-- Name: research_evidence research_evidence_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_evidence
    ADD CONSTRAINT research_evidence_pkey PRIMARY KEY (evidence_id);


--
-- Name: research_evidence research_evidence_resource_type_resource_id_kind_reference__key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_evidence
    ADD CONSTRAINT research_evidence_resource_type_resource_id_kind_reference__key UNIQUE (resource_type, resource_id, kind, reference_id);


--
-- Name: research_experiment_results research_experiment_results_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_experiment_results
    ADD CONSTRAINT research_experiment_results_pkey PRIMARY KEY (result_id);


--
-- Name: research_experiment_runs research_experiment_runs_experiment_id_sequence_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_experiment_runs
    ADD CONSTRAINT research_experiment_runs_experiment_id_sequence_key UNIQUE (experiment_id, sequence);


--
-- Name: research_experiment_runs research_experiment_runs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_experiment_runs
    ADD CONSTRAINT research_experiment_runs_pkey PRIMARY KEY (experiment_id, execution_key);


--
-- Name: research_experiments research_experiments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_experiments
    ADD CONSTRAINT research_experiments_pkey PRIMARY KEY (experiment_id);


--
-- Name: research_historical_replay_reports research_historical_replay_re_project_id_dataset_snapshot_h_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_historical_replay_reports
    ADD CONSTRAINT research_historical_replay_re_project_id_dataset_snapshot_h_key UNIQUE (project_id, dataset_snapshot_hash, report_hash);


--
-- Name: research_historical_replay_reports research_historical_replay_reports_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_historical_replay_reports
    ADD CONSTRAINT research_historical_replay_reports_pkey PRIMARY KEY (report_id);


--
-- Name: research_hypotheses research_hypotheses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypotheses
    ADD CONSTRAINT research_hypotheses_pkey PRIMARY KEY (hypothesis_id);


--
-- Name: research_hypothesis_causal_links research_hypothesis_causal_links_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_causal_links
    ADD CONSTRAINT research_hypothesis_causal_links_pkey PRIMARY KEY (hypothesis_id, sequence);


--
-- Name: research_hypothesis_confounders research_hypothesis_confounders_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_confounders
    ADD CONSTRAINT research_hypothesis_confounders_pkey PRIMARY KEY (hypothesis_id, sequence);


--
-- Name: research_hypothesis_evidence research_hypothesis_evidence_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_evidence
    ADD CONSTRAINT research_hypothesis_evidence_pkey PRIMARY KEY (hypothesis_id, evidence_id, evidence_role);


--
-- Name: research_hypothesis_failure_conditions research_hypothesis_failure_conditio_hypothesis_id_sequence_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_failure_conditions
    ADD CONSTRAINT research_hypothesis_failure_conditio_hypothesis_id_sequence_key UNIQUE (hypothesis_id, sequence);


--
-- Name: research_hypothesis_failure_conditions research_hypothesis_failure_conditions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_failure_conditions
    ADD CONSTRAINT research_hypothesis_failure_conditions_pkey PRIMARY KEY (failure_condition_id);


--
-- Name: research_hypothesis_falsification_conditions research_hypothesis_falsification_conditions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_falsification_conditions
    ADD CONSTRAINT research_hypothesis_falsification_conditions_pkey PRIMARY KEY (hypothesis_id, sequence);


--
-- Name: research_hypothesis_interaction_variables research_hypothesis_interactio_interaction_id_variable_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_interaction_variables
    ADD CONSTRAINT research_hypothesis_interactio_interaction_id_variable_code_key UNIQUE (interaction_id, variable_code);


--
-- Name: research_hypothesis_interaction_variables research_hypothesis_interaction_variables_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_interaction_variables
    ADD CONSTRAINT research_hypothesis_interaction_variables_pkey PRIMARY KEY (interaction_id, sequence);


--
-- Name: research_hypothesis_interactions research_hypothesis_interactions_hypothesis_id_sequence_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_interactions
    ADD CONSTRAINT research_hypothesis_interactions_hypothesis_id_sequence_key UNIQUE (hypothesis_id, sequence);


--
-- Name: research_hypothesis_interactions research_hypothesis_interactions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_interactions
    ADD CONSTRAINT research_hypothesis_interactions_pkey PRIMARY KEY (interaction_id);


--
-- Name: research_hypothesis_temporal_features research_hypothesis_temporal_features_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_temporal_features
    ADD CONSTRAINT research_hypothesis_temporal_features_pkey PRIMARY KEY (hypothesis_id, sequence);


--
-- Name: research_hypothesis_variables research_hypothesis_variables_hypothesis_id_variable_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_variables
    ADD CONSTRAINT research_hypothesis_variables_hypothesis_id_variable_code_key UNIQUE (hypothesis_id, variable_code);


--
-- Name: research_hypothesis_variables research_hypothesis_variables_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_variables
    ADD CONSTRAINT research_hypothesis_variables_pkey PRIMARY KEY (hypothesis_id, sequence);


--
-- Name: research_knowledge_claims research_knowledge_claims_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_knowledge_claims
    ADD CONSTRAINT research_knowledge_claims_pkey PRIMARY KEY (claim_id);


--
-- Name: research_operating_region_results research_operating_region_results_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_operating_region_results
    ADD CONSTRAINT research_operating_region_results_pkey PRIMARY KEY (operating_region_id, result_id);


--
-- Name: research_operating_regions research_operating_regions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_operating_regions
    ADD CONSTRAINT research_operating_regions_pkey PRIMARY KEY (operating_region_id);


--
-- Name: research_project_members research_project_members_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_project_members
    ADD CONSTRAINT research_project_members_pkey PRIMARY KEY (project_id, user_id);


--
-- Name: research_rollback_drills research_rollback_drills_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_rollback_drills
    ADD CONSTRAINT research_rollback_drills_pkey PRIMARY KEY (drill_id);


--
-- Name: research_shadow_recommendations research_shadow_recommendations_experiment_suggestion_execution; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_shadow_recommendations
    ADD CONSTRAINT research_shadow_recommendations_experiment_suggestion_execution UNIQUE (experiment_id, suggestion_execution_key);


--
-- Name: research_shadow_recommendations research_shadow_recommendations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_shadow_recommendations
    ADD CONSTRAINT research_shadow_recommendations_pkey PRIMARY KEY (recommendation_id);


--
-- Name: research_shadow_recommendations research_shadow_recommendations_project_actual_execution_key_ke; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_shadow_recommendations
    ADD CONSTRAINT research_shadow_recommendations_project_actual_execution_key_ke UNIQUE (project_id, actual_execution_key);


--
-- Name: research_transfer_assessments research_transfer_assessments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_transfer_assessments
    ADD CONSTRAINT research_transfer_assessments_pkey PRIMARY KEY (assessment_id);


--
-- Name: research_transfer_assessments research_transfer_assessments_project_source_region_record_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_transfer_assessments
    ADD CONSTRAINT research_transfer_assessments_project_source_region_record_key UNIQUE (project_id, source_operating_region_id, record_hash);


--
-- Name: research_validation_preregistrations research_validation_preregistration_project_id_content_hash_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_validation_preregistrations
    ADD CONSTRAINT research_validation_preregistration_project_id_content_hash_key UNIQUE (project_id, content_hash);


--
-- Name: research_validation_preregistrations research_validation_preregistrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_validation_preregistrations
    ADD CONSTRAINT research_validation_preregistrations_pkey PRIMARY KEY (preregistration_id);


--
-- Name: research_validation_preregistrations research_validation_preregistrations_project_id_version_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_validation_preregistrations
    ADD CONSTRAINT research_validation_preregistrations_project_id_version_key UNIQUE (project_id, version);


--
-- Name: scenario_packages scenario_packages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.scenario_packages
    ADD CONSTRAINT scenario_packages_pkey PRIMARY KEY (package_id, version);


--
-- Name: dataset_quality_validation_reports dataset_quality_validation_reports_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.dataset_quality_validation_reports
    ADD CONSTRAINT dataset_quality_validation_reports_pkey PRIMARY KEY (report_id);


--
-- Name: signal_definitions signal_definitions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.signal_definitions
    ADD CONSTRAINT signal_definitions_pkey PRIMARY KEY (data_model_id, data_model_version, signal_code);


--
-- Name: tooling_assemblies tooling_assemblies_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_assemblies
    ADD CONSTRAINT tooling_assemblies_pkey PRIMARY KEY (tooling_assembly_id);


--
-- Name: tooling_assembly_revisions tooling_assembly_revisions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_assembly_revisions
    ADD CONSTRAINT tooling_assembly_revisions_pkey PRIMARY KEY (assembly_revision_id);


--
-- Name: tooling_assembly_revisions tooling_assembly_revisions_tooling_assembly_id_revision_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_assembly_revisions
    ADD CONSTRAINT tooling_assembly_revisions_tooling_assembly_id_revision_key UNIQUE (tooling_assembly_id, revision);


--
-- Name: tooling_component_types tooling_component_types_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_component_types
    ADD CONSTRAINT tooling_component_types_pkey PRIMARY KEY (component_type_code);


--
-- Name: tooling_components tooling_components_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_components
    ADD CONSTRAINT tooling_components_pkey PRIMARY KEY (component_id);


--
-- Name: tooling_components tooling_components_serial_no_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_components
    ADD CONSTRAINT tooling_components_serial_no_key UNIQUE (serial_no);


--
-- Name: tooling_installations tooling_installations_command_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_installations
    ADD CONSTRAINT tooling_installations_command_id_key UNIQUE (command_id);


--
-- Name: tooling_installations tooling_installations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_installations
    ADD CONSTRAINT tooling_installations_pkey PRIMARY KEY (installation_id);


--
-- Name: tooling_types tooling_types_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_types
    ADD CONSTRAINT tooling_types_pkey PRIMARY KEY (tooling_type_code, version);


--
-- Name: tooling_usage_counters tooling_usage_counters_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_usage_counters
    ADD CONSTRAINT tooling_usage_counters_pkey PRIMARY KEY (tooling_installation_id);


--
-- Name: training_dataset_versions training_dataset_versions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.training_dataset_versions
    ADD CONSTRAINT training_dataset_versions_pkey PRIMARY KEY (dataset_id, version);


--
-- Name: user_sessions user_sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_sessions
    ADD CONSTRAINT user_sessions_pkey PRIMARY KEY (token_hash);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (user_id);


--
-- Name: knowledge_sources ux_knowledge_sources_project_sha256; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_sources
    ADD CONSTRAINT ux_knowledge_sources_project_sha256 UNIQUE (project_id, sha256);


--
-- Name: mechanism_claims ux_mechanism_claims_id_project; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claims
    ADD CONSTRAINT ux_mechanism_claims_id_project UNIQUE (claim_id, project_id);


--
-- Name: idx_case_level_eval_case_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_case_level_eval_case_time ON public.case_level_evaluations USING btree (case_id, evaluated_at DESC);


--
-- Name: idx_data_source_instances_edge_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_data_source_instances_edge_status ON public.data_source_instances USING btree (edge_id, status);


--
-- Name: idx_dataset_quality_validation_dataset; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dataset_quality_validation_dataset ON public.dataset_quality_validation_reports USING btree (dataset_id, dataset_version, created_at DESC);


--
-- Name: idx_edge_runtime_status_history_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_edge_runtime_status_history_time ON public.edge_runtime_status_history USING btree (edge_id, recorded_at DESC);


--
-- Name: idx_execution_analysis_backfill_jobs_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_execution_analysis_backfill_jobs_status ON public.execution_analysis_backfill_jobs USING btree (status, created_at);


--
-- Name: idx_execution_analysis_materializations_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_execution_analysis_materializations_status ON public.execution_analysis_materializations USING btree (status, computed_at);


--
-- Name: idx_execution_features_lookup; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_execution_features_lookup ON public.execution_features USING btree (signal_code, phase_code, feature_code, execution_id);


--
-- Name: idx_execution_phases_code_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_execution_phases_code_time ON public.execution_phases USING btree (phase_code, started_at);


--
-- Name: idx_feature_definitions_phase; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_feature_definitions_phase ON public.feature_definitions USING btree (phase_code);


--
-- Name: idx_golden_question_cases_status_updated; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_golden_question_cases_status_updated ON public.golden_question_cases USING btree (status, updated_at DESC);


--
-- Name: idx_golden_question_evaluations_case_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_golden_question_evaluations_case_time ON public.golden_question_evaluations USING btree (case_id, case_version, evaluated_at DESC);


--
-- Name: idx_ingestion_tasks_edge_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_ingestion_tasks_edge_status ON public.ingestion_tasks USING btree (edge_id, status);


--
-- Name: idx_inspection_attachments_sha256; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inspection_attachments_sha256 ON public.inspection_attachments USING btree (sha256);


--
-- Name: idx_inspection_audit_attachment_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inspection_audit_attachment_time ON public.inspection_audit_log USING btree (attachment_id, occurred_at DESC);


--
-- Name: idx_inspection_audit_record_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inspection_audit_record_time ON public.inspection_audit_log USING btree (inspection_record_id, occurred_at DESC);


--
-- Name: idx_inspection_records_definition_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inspection_records_definition_time ON public.inspection_records USING btree (definition_code, measured_at DESC);


--
-- Name: idx_inspection_records_execution_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inspection_records_execution_time ON public.inspection_records USING btree (execution_id, measured_at DESC);


--
-- Name: idx_inspection_records_one_correction; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_inspection_records_one_correction ON public.inspection_records USING btree (supersedes_record_id) WHERE (supersedes_record_id IS NOT NULL);


--
-- Name: idx_inspection_records_outcome_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inspection_records_outcome_time ON public.inspection_records USING btree (outcome, measured_at DESC);


--
-- Name: idx_inspection_records_output_item_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inspection_records_output_item_time ON public.inspection_records USING btree (output_item_id, measured_at DESC);


--
-- Name: idx_inspection_reviews_execution_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inspection_reviews_execution_time ON public.inspection_reviews USING btree (execution_id, reviewed_at DESC);


--
-- Name: idx_inspection_reviews_record_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inspection_reviews_record_time ON public.inspection_reviews USING btree (inspection_record_id, reviewed_at DESC);


--
-- Name: idx_inspection_scopes_subject_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inspection_scopes_subject_time ON public.inspection_scopes USING btree (subject_id, to_at DESC);


--
-- Name: idx_model_drift_readings_model; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_model_drift_readings_model ON public.model_drift_readings USING btree (model_id, model_version, created_at DESC);


--
-- Name: idx_phase_mappings_lookup; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_phase_mappings_lookup ON public.phase_mappings USING btree (process_specification_id, process_specification_version, process_template, process_step);


--
-- Name: idx_problem_cases_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_problem_cases_status ON public.problem_cases USING btree (status, updated_at DESC);


--
-- Name: idx_process_analysis_plans_model; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_process_analysis_plans_model ON public.process_analysis_plans USING btree (data_model_id, data_model_version);


--
-- Name: idx_process_research_audit_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_process_research_audit_project ON public.process_research_audit USING btree (project_id, created_at DESC);


--
-- Name: idx_process_research_projects_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_process_research_projects_status ON public.process_research_projects USING btree (status, updated_at DESC);


--
-- Name: idx_process_specification_versions_model; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_process_specification_versions_model ON public.process_specification_versions USING btree (data_model_id, data_model_version);


--
-- Name: idx_production_contexts_active_equipment; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_production_contexts_active_equipment ON public.production_contexts USING btree (equipment_id) WHERE (valid_to IS NULL);


--
-- Name: idx_production_contexts_command_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_production_contexts_command_id ON public.production_contexts USING btree (command_id) WHERE (command_id IS NOT NULL);


--
-- Name: idx_production_contexts_equipment_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_production_contexts_equipment_time ON public.production_contexts USING btree (equipment_id, valid_from, valid_to);


--
-- Name: idx_production_events_context; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_production_events_context ON public.production_events USING gin (context);


--
-- Name: idx_production_events_execution; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_production_events_execution ON public.production_events USING btree (execution_id, occurred_at);


--
-- Name: idx_production_events_ingest; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_production_events_ingest ON public.production_events USING btree (ingest_id);


--
-- Name: idx_production_events_subject_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_production_events_subject_time ON public.production_events USING btree (subject_type, subject_id, occurred_at DESC);


--
-- Name: idx_production_events_type_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_production_events_type_time ON public.production_events USING btree (event_type, occurred_at DESC);


--
-- Name: idx_production_events_site_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_production_events_site_time ON public.production_events USING btree (site_id, occurred_at DESC);


--
-- Name: idx_research_asset_audit_resource; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_asset_audit_resource ON public.research_asset_audit USING btree (resource_type, resource_id, created_at);


--
-- Name: idx_research_evidence_project_resource; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_evidence_project_resource ON public.research_evidence USING btree (project_id, resource_type, resource_id, created_at DESC);


--
-- Name: idx_research_experiment_results_experiment; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_experiment_results_experiment ON public.research_experiment_results USING btree (experiment_id, recorded_at DESC);


--
-- Name: idx_research_experiment_results_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_experiment_results_project ON public.research_experiment_results USING btree (project_id, recorded_at DESC);


--
-- Name: idx_research_experiments_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_experiments_project ON public.research_experiments USING btree (project_id, updated_at DESC);


--
-- Name: idx_research_historical_replay_reports_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_historical_replay_reports_project ON public.research_historical_replay_reports USING btree (project_id, generated_at DESC);


--
-- Name: idx_research_hypotheses_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_hypotheses_project ON public.research_hypotheses USING btree (project_id, updated_at DESC);


--
-- Name: idx_research_knowledge_claims_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_knowledge_claims_project ON public.research_knowledge_claims USING btree (project_id, updated_at DESC);


--
-- Name: idx_research_operating_region_results_result; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_operating_region_results_result ON public.research_operating_region_results USING btree (result_id, operating_region_id);


--
-- Name: idx_research_operating_regions_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_operating_regions_project ON public.research_operating_regions USING btree (project_id, updated_at DESC);


--
-- Name: idx_research_project_members_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_project_members_user ON public.research_project_members USING btree (user_id, project_id);


--
-- Name: idx_research_shadow_recommendations_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_research_shadow_recommendations_project ON public.research_shadow_recommendations USING btree (project_id, decided_at DESC);


--
-- Name: idx_scenario_packages_model; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_scenario_packages_model ON public.scenario_packages USING btree (data_model_id, data_model_version);


--
-- Name: idx_tooling_components_component_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_tooling_components_component_type ON public.tooling_components USING btree (component_type_code);


--
-- Name: idx_tooling_installations_active_equipment; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_tooling_installations_active_equipment ON public.tooling_installations USING btree (equipment_id) WHERE (removed_at IS NULL);


--
-- Name: idx_tooling_installations_active_revision; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX idx_tooling_installations_active_revision ON public.tooling_installations USING btree (assembly_revision_id) WHERE (removed_at IS NULL);


--
-- Name: idx_tooling_installations_equipment_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_tooling_installations_equipment_time ON public.tooling_installations USING btree (equipment_id, installed_at, removed_at);


--
-- Name: idx_user_sessions_expiry; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_user_sessions_expiry ON public.user_sessions USING btree (expires_at);


--
-- Name: idx_user_sessions_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_user_sessions_user ON public.user_sessions USING btree (user_id);


--
-- Name: ix_agent_runs_user_entry_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_agent_runs_user_entry_created ON public.agent_runs USING btree (user_id, entry_point, created_at DESC, run_id);


--
-- Name: ix_agent_stream_events_run_sequence; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_agent_stream_events_run_sequence ON public.agent_stream_events USING btree (run_id, sequence);


--
-- Name: ix_execution_analysis_backfill_jobs_claim; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_execution_analysis_backfill_jobs_claim ON public.execution_analysis_backfill_jobs USING btree (status, available_at, created_at);


--
-- Name: ix_execution_analysis_recompute_jobs_claim; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_execution_analysis_recompute_jobs_claim ON public.execution_analysis_recompute_jobs USING btree (status, available_at, updated_at);


--
-- Name: ix_knowledge_extraction_jobs_claim; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_knowledge_extraction_jobs_claim ON public.knowledge_extraction_jobs USING btree (status, available_at, updated_at);


--
-- Name: ix_knowledge_extraction_jobs_running_lease; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_knowledge_extraction_jobs_running_lease ON public.knowledge_extraction_jobs USING btree (leased_at) WHERE (status = 'running'::text);


--
-- Name: ix_knowledge_fragments_source; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_knowledge_fragments_source ON public.knowledge_fragments USING btree (source_id, updated_at DESC);


--
-- Name: ix_knowledge_sources_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_knowledge_sources_project ON public.knowledge_sources USING btree (project_id, updated_at DESC);


--
-- Name: ix_mechanism_claim_conflicts_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_mechanism_claim_conflicts_project ON public.mechanism_claim_conflicts USING btree (project_id, status, created_at DESC);


--
-- Name: ix_mechanism_claim_lifecycle_claim; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_mechanism_claim_lifecycle_claim ON public.mechanism_claim_lifecycle_decisions USING btree (claim_id, decided_at DESC);


--
-- Name: ix_mechanism_claims_project_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_mechanism_claims_project_status ON public.mechanism_claims USING btree (project_id, status, updated_at DESC);


--
-- Name: ix_process_research_audit_project_page; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_process_research_audit_project_page ON public.process_research_audit USING btree (project_id, created_at DESC, entry_id DESC);


--
-- Name: ix_process_sample_frames_execution; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_process_sample_frames_execution ON public.process_sample_frames USING btree (execution_id, occurred_at);


--
-- Name: ix_process_sample_frames_subject; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_process_sample_frames_subject ON public.process_sample_frames USING btree (subject_type, subject_id, occurred_at);


--
-- Name: ix_process_sample_frames_site_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_process_sample_frames_site_time ON public.process_sample_frames USING btree (site_id, occurred_at DESC);


--
-- Name: ix_process_sample_values_point_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_process_sample_values_point_time ON public.process_sample_values USING btree (point_key, occurred_at DESC);


--
-- Name: ix_research_experiment_results_project_page; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_research_experiment_results_project_page ON public.research_experiment_results USING btree (project_id, recorded_at DESC, result_id DESC);


--
-- Name: ix_research_experiments_project_page; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_research_experiments_project_page ON public.research_experiments USING btree (project_id, updated_at DESC, experiment_id DESC);


--
-- Name: ix_research_historical_replay_reports_project_page; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_research_historical_replay_reports_project_page ON public.research_historical_replay_reports USING btree (project_id, generated_at DESC, report_id DESC);


--
-- Name: ix_research_rollback_drills_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_research_rollback_drills_project ON public.research_rollback_drills USING btree (project_id, recorded_at DESC, drill_id);


--
-- Name: ix_research_shadow_recommendations_project_page; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_research_shadow_recommendations_project_page ON public.research_shadow_recommendations USING btree (project_id, decided_at DESC, recommendation_id DESC);


--
-- Name: ix_research_transfer_assessments_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_research_transfer_assessments_project ON public.research_transfer_assessments USING btree (project_id, created_at DESC, assessment_id);


--
-- Name: ix_research_transfer_assessments_source; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_research_transfer_assessments_source ON public.research_transfer_assessments USING btree (source_project_id, source_operating_region_id, created_at DESC);


--
-- Name: ix_research_validation_preregistrations_project; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_research_validation_preregistrations_project ON public.research_validation_preregistrations USING btree (project_id, version DESC);


--
-- Name: process_sample_frames_occurred_at_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX process_sample_frames_occurred_at_idx ON public.process_sample_frames USING btree (occurred_at DESC);


--
-- Name: process_sample_values_occurred_at_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX process_sample_values_occurred_at_idx ON public.process_sample_values USING btree (occurred_at DESC);


--
-- Name: production_events_occurred_at_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX production_events_occurred_at_idx ON public.production_events USING btree (occurred_at DESC);


--
-- Name: uq_data_source_instances_published; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_data_source_instances_published ON public.data_source_instances USING btree (data_source_id) WHERE (status = 'published'::text);


--
-- Name: uq_ingestion_task_bindings_published; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_ingestion_task_bindings_published ON public.ingestion_task_bindings USING btree (task_id) WHERE (status = 'published'::text);


--
-- Name: uq_ingestion_task_templates_published; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_ingestion_task_templates_published ON public.ingestion_task_templates USING btree (template_id) WHERE (status = 'published'::text);


--
-- Name: uq_ingestion_tasks_published; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_ingestion_tasks_published ON public.ingestion_tasks USING btree (task_id) WHERE (status = 'published'::text);


--
-- Name: uq_mechanism_fusion_active; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_mechanism_fusion_active ON public.mechanism_fusion_definitions USING btree (fusion_id) WHERE (status = 'active'::text);


--
-- Name: uq_mechanism_model_active; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_mechanism_model_active ON public.mechanism_model_versions USING btree (model_id) WHERE (status = 'active'::text);


--
-- Name: uq_process_model_active; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_process_model_active ON public.process_model_versions USING btree (model_id) WHERE (status = 'active'::text);


--
-- Name: ux_collection_points_key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_collection_points_key ON public.collection_points USING btree (point_key);


--
-- Name: ux_mechanism_claim_lifecycle_evidence; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_mechanism_claim_lifecycle_evidence ON public.mechanism_claim_lifecycle_decisions USING btree (claim_id, reference_id) WHERE (reference_id IS NOT NULL);


--
-- Name: ux_mechanism_conflict_pair; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_mechanism_conflict_pair ON public.mechanism_claim_conflicts USING btree (project_id, LEAST(left_claim_id, right_claim_id), GREATEST(left_claim_id, right_claim_id), (
CASE
    WHEN (left_claim_id < right_claim_id) THEN left_claim_version
    ELSE right_claim_version
END), (
CASE
    WHEN (left_claim_id < right_claim_id) THEN right_claim_version
    ELSE left_claim_version
END), conflict_kind) WHERE (status = 'open'::text);


--
-- Name: ux_process_sample_frames_event; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_process_sample_frames_event ON public.process_sample_frames USING btree (event_id, occurred_at);


--
-- Name: ux_process_sample_frames_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_process_sample_frames_id ON public.process_sample_frames USING btree (frame_id, occurred_at);


--
-- Name: ux_process_sample_values_point; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_process_sample_values_point ON public.process_sample_values USING btree (frame_id, point_key, occurred_at);


--
-- Name: ux_research_experiment_results_experiment; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_research_experiment_results_experiment ON public.research_experiment_results USING btree (experiment_id);


--
-- Name: ux_users_username_lower; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_users_username_lower ON public.users USING btree (username_lower);


--
-- Name: agent_stream_events agent_stream_events_run_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.agent_stream_events
    ADD CONSTRAINT agent_stream_events_run_id_fkey FOREIGN KEY (run_id) REFERENCES public.agent_runs(run_id) ON DELETE CASCADE;


--
-- Name: case_level_evaluations case_level_evaluations_case_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.case_level_evaluations
    ADD CONSTRAINT case_level_evaluations_case_id_fkey FOREIGN KEY (case_id) REFERENCES public.problem_cases(case_id) ON DELETE CASCADE;


--
-- Name: edge_runtime_status_history edge_runtime_status_history_edge_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.edge_runtime_status_history
    ADD CONSTRAINT edge_runtime_status_history_edge_id_fkey FOREIGN KEY (edge_id) REFERENCES public.platform_edges(edge_id) ON DELETE CASCADE;


--
-- Name: golden_question_evaluations fk_golden_question_evaluation_agent_run; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.golden_question_evaluations
    ADD CONSTRAINT fk_golden_question_evaluation_agent_run FOREIGN KEY (agent_run_id) REFERENCES public.agent_runs(run_id) ON DELETE RESTRICT;


--
-- Name: knowledge_sources fk_knowledge_sources_project; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_sources
    ADD CONSTRAINT fk_knowledge_sources_project FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id) ON DELETE CASCADE;


--
-- Name: mechanism_claim_conflicts fk_mechanism_conflict_left_project; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_conflicts
    ADD CONSTRAINT fk_mechanism_conflict_left_project FOREIGN KEY (left_claim_id, project_id) REFERENCES public.mechanism_claims(claim_id, project_id);


--
-- Name: mechanism_claim_conflicts fk_mechanism_conflict_right_project; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_conflicts
    ADD CONSTRAINT fk_mechanism_conflict_right_project FOREIGN KEY (right_claim_id, project_id) REFERENCES public.mechanism_claims(claim_id, project_id);


--
-- Name: recommendation_knowledge_usage fk_recommendation_knowledge_usage_experiment; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.recommendation_knowledge_usage
    ADD CONSTRAINT fk_recommendation_knowledge_usage_experiment FOREIGN KEY (recommendation_id) REFERENCES public.research_experiments(experiment_id) ON DELETE CASCADE;


--
-- Name: golden_question_evaluations golden_question_evaluations_case_id_case_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.golden_question_evaluations
    ADD CONSTRAINT golden_question_evaluations_case_id_case_version_fkey FOREIGN KEY (case_id, case_version) REFERENCES public.golden_question_cases(case_id, version) ON DELETE RESTRICT;


--
-- Name: ingestion_task_bindings ingestion_task_bindings_data_source_id_data_source_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ingestion_task_bindings
    ADD CONSTRAINT ingestion_task_bindings_data_source_id_data_source_version_fkey FOREIGN KEY (data_source_id, data_source_version) REFERENCES public.data_source_instances(data_source_id, version);


--
-- Name: ingestion_task_bindings ingestion_task_bindings_template_id_template_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ingestion_task_bindings
    ADD CONSTRAINT ingestion_task_bindings_template_id_template_version_fkey FOREIGN KEY (template_id, template_version) REFERENCES public.ingestion_task_templates(template_id, version);


--
-- Name: knowledge_extraction_jobs knowledge_extraction_jobs_source_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_extraction_jobs
    ADD CONSTRAINT knowledge_extraction_jobs_source_id_fkey FOREIGN KEY (source_id) REFERENCES public.knowledge_sources(source_id) ON DELETE CASCADE;


--
-- Name: knowledge_fragment_values knowledge_fragment_values_fragment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_fragment_values
    ADD CONSTRAINT knowledge_fragment_values_fragment_id_fkey FOREIGN KEY (fragment_id) REFERENCES public.knowledge_fragments(record_id) ON DELETE CASCADE;


--
-- Name: knowledge_source_context knowledge_source_context_source_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_source_context
    ADD CONSTRAINT knowledge_source_context_source_id_fkey FOREIGN KEY (source_id) REFERENCES public.knowledge_sources(source_id) ON DELETE CASCADE;


--
-- Name: mechanism_claim_applicability mechanism_claim_applicability_claim_id_claim_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_applicability
    ADD CONSTRAINT mechanism_claim_applicability_claim_id_claim_version_fkey FOREIGN KEY (claim_id, claim_version) REFERENCES public.mechanism_claim_versions(claim_id, version) ON DELETE CASCADE;


--
-- Name: mechanism_claim_conflicts mechanism_claim_conflicts_left_claim_id_left_claim_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_conflicts
    ADD CONSTRAINT mechanism_claim_conflicts_left_claim_id_left_claim_version_fkey FOREIGN KEY (left_claim_id, left_claim_version) REFERENCES public.mechanism_claim_versions(claim_id, version);


--
-- Name: mechanism_claim_conflicts mechanism_claim_conflicts_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_conflicts
    ADD CONSTRAINT mechanism_claim_conflicts_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id) ON DELETE CASCADE;


--
-- Name: mechanism_claim_conflicts mechanism_claim_conflicts_right_claim_id_right_claim_versi_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_conflicts
    ADD CONSTRAINT mechanism_claim_conflicts_right_claim_id_right_claim_versi_fkey FOREIGN KEY (right_claim_id, right_claim_version) REFERENCES public.mechanism_claim_versions(claim_id, version);


--
-- Name: mechanism_claim_constraints mechanism_claim_constraints_claim_id_claim_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_constraints
    ADD CONSTRAINT mechanism_claim_constraints_claim_id_claim_version_fkey FOREIGN KEY (claim_id, claim_version) REFERENCES public.mechanism_claim_versions(claim_id, version) ON DELETE CASCADE;


--
-- Name: mechanism_claim_evidence mechanism_claim_evidence_claim_id_claim_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_evidence
    ADD CONSTRAINT mechanism_claim_evidence_claim_id_claim_version_fkey FOREIGN KEY (claim_id, claim_version) REFERENCES public.mechanism_claim_versions(claim_id, version) ON DELETE CASCADE;


--
-- Name: mechanism_claim_lifecycle_decisions mechanism_claim_lifecycle_validation_hypothesis_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_lifecycle_decisions
    ADD CONSTRAINT mechanism_claim_lifecycle_validation_hypothesis_fkey FOREIGN KEY (validation_hypothesis_id) REFERENCES public.research_hypotheses(hypothesis_id);


--
-- Name: mechanism_claim_lifecycle_decisions mechanism_claim_lifecycle_decisions_claim_id_claim_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_lifecycle_decisions
    ADD CONSTRAINT mechanism_claim_lifecycle_decisions_claim_id_claim_version_fkey FOREIGN KEY (claim_id, claim_version) REFERENCES public.mechanism_claim_versions(claim_id, version) ON DELETE CASCADE;


--
-- Name: mechanism_claim_reviews mechanism_claim_reviews_claim_id_claim_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_reviews
    ADD CONSTRAINT mechanism_claim_reviews_claim_id_claim_version_fkey FOREIGN KEY (claim_id, claim_version) REFERENCES public.mechanism_claim_versions(claim_id, version) ON DELETE CASCADE;


--
-- Name: mechanism_claim_variables mechanism_claim_variables_claim_id_claim_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_variables
    ADD CONSTRAINT mechanism_claim_variables_claim_id_claim_version_fkey FOREIGN KEY (claim_id, claim_version) REFERENCES public.mechanism_claim_versions(claim_id, version) ON DELETE CASCADE;


--
-- Name: mechanism_claim_versions mechanism_claim_versions_claim_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claim_versions
    ADD CONSTRAINT mechanism_claim_versions_claim_id_fkey FOREIGN KEY (claim_id) REFERENCES public.mechanism_claims(claim_id) ON DELETE CASCADE;


--
-- Name: mechanism_claims mechanism_claims_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_claims
    ADD CONSTRAINT mechanism_claims_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id) ON DELETE CASCADE;


--
-- Name: mechanism_fusion_definitions mechanism_fusion_definitions_mechanism_model_id_mechanism__fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mechanism_fusion_definitions
    ADD CONSTRAINT mechanism_fusion_definitions_mechanism_model_id_mechanism__fkey FOREIGN KEY (mechanism_model_id, mechanism_model_version) REFERENCES public.mechanism_model_versions(model_id, version);


--
-- Name: model_drift_readings model_drift_readings_model_id_model_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.model_drift_readings
    ADD CONSTRAINT model_drift_readings_model_id_model_version_fkey FOREIGN KEY (model_id, model_version) REFERENCES public.process_model_versions(model_id, version);


--
-- Name: model_evaluations model_evaluations_model_id_model_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.model_evaluations
    ADD CONSTRAINT model_evaluations_model_id_model_version_fkey FOREIGN KEY (model_id, model_version) REFERENCES public.process_model_versions(model_id, version);


--
-- Name: knowledge_fragments knowledge_fragments_source_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.knowledge_fragments
    ADD CONSTRAINT knowledge_fragments_source_id_fkey FOREIGN KEY (source_id) REFERENCES public.knowledge_sources(source_id);


--
-- Name: process_model_versions process_model_versions_dataset_id_dataset_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.process_model_versions
    ADD CONSTRAINT process_model_versions_dataset_id_dataset_version_fkey FOREIGN KEY (dataset_id, dataset_version) REFERENCES public.training_dataset_versions(dataset_id, version);


--
-- Name: process_research_audit process_research_audit_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.process_research_audit
    ADD CONSTRAINT process_research_audit_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id);


--
-- Name: production_contexts production_contexts_tooling_installation_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.production_contexts
    ADD CONSTRAINT production_contexts_tooling_installation_id_fkey FOREIGN KEY (tooling_installation_id) REFERENCES public.tooling_installations(installation_id);


--
-- Name: recommendation_knowledge_usage recommendation_knowledge_usage_claim_id_claim_version_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.recommendation_knowledge_usage
    ADD CONSTRAINT recommendation_knowledge_usage_claim_id_claim_version_fkey FOREIGN KEY (claim_id, claim_version) REFERENCES public.mechanism_claim_versions(claim_id, version);


--
-- Name: research_evidence research_evidence_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_evidence
    ADD CONSTRAINT research_evidence_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id) ON DELETE CASCADE;


--
-- Name: research_experiment_results research_experiment_results_experiment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_experiment_results
    ADD CONSTRAINT research_experiment_results_experiment_id_fkey FOREIGN KEY (experiment_id) REFERENCES public.research_experiments(experiment_id);


--
-- Name: research_experiment_results research_experiment_results_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_experiment_results
    ADD CONSTRAINT research_experiment_results_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id);


--
-- Name: research_experiment_runs research_experiment_runs_experiment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_experiment_runs
    ADD CONSTRAINT research_experiment_runs_experiment_id_fkey FOREIGN KEY (experiment_id) REFERENCES public.research_experiments(experiment_id) ON DELETE CASCADE;


--
-- Name: research_experiments research_experiments_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_experiments
    ADD CONSTRAINT research_experiments_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id);


--
-- Name: research_historical_replay_reports research_historical_replay_reports_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_historical_replay_reports
    ADD CONSTRAINT research_historical_replay_reports_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id);


--
-- Name: research_hypotheses research_hypotheses_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypotheses
    ADD CONSTRAINT research_hypotheses_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id);


--
-- Name: research_hypothesis_causal_links research_hypothesis_causal_links_hypothesis_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_causal_links
    ADD CONSTRAINT research_hypothesis_causal_links_hypothesis_id_fkey FOREIGN KEY (hypothesis_id) REFERENCES public.research_hypotheses(hypothesis_id) ON DELETE CASCADE;


--
-- Name: research_hypothesis_confounders research_hypothesis_confounders_hypothesis_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_confounders
    ADD CONSTRAINT research_hypothesis_confounders_hypothesis_id_fkey FOREIGN KEY (hypothesis_id) REFERENCES public.research_hypotheses(hypothesis_id) ON DELETE CASCADE;


--
-- Name: research_hypothesis_evidence research_hypothesis_evidence_hypothesis_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_evidence
    ADD CONSTRAINT research_hypothesis_evidence_hypothesis_id_fkey FOREIGN KEY (hypothesis_id) REFERENCES public.research_hypotheses(hypothesis_id) ON DELETE CASCADE;


--
-- Name: research_hypothesis_evidence research_hypothesis_evidence_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_evidence
    ADD CONSTRAINT research_hypothesis_evidence_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id) ON DELETE CASCADE;


--
-- Name: research_hypothesis_failure_conditions research_hypothesis_failure_conditions_hypothesis_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_failure_conditions
    ADD CONSTRAINT research_hypothesis_failure_conditions_hypothesis_id_fkey FOREIGN KEY (hypothesis_id) REFERENCES public.research_hypotheses(hypothesis_id) ON DELETE CASCADE;


--
-- Name: research_hypothesis_falsification_conditions research_hypothesis_falsification_conditions_hypothesis_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_falsification_conditions
    ADD CONSTRAINT research_hypothesis_falsification_conditions_hypothesis_id_fkey FOREIGN KEY (hypothesis_id) REFERENCES public.research_hypotheses(hypothesis_id) ON DELETE CASCADE;


--
-- Name: research_hypothesis_interaction_variables research_hypothesis_interaction_variables_interaction_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_interaction_variables
    ADD CONSTRAINT research_hypothesis_interaction_variables_interaction_id_fkey FOREIGN KEY (interaction_id) REFERENCES public.research_hypothesis_interactions(interaction_id) ON DELETE CASCADE;


--
-- Name: research_hypothesis_interactions research_hypothesis_interactions_hypothesis_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_interactions
    ADD CONSTRAINT research_hypothesis_interactions_hypothesis_id_fkey FOREIGN KEY (hypothesis_id) REFERENCES public.research_hypotheses(hypothesis_id) ON DELETE CASCADE;


--
-- Name: research_hypothesis_temporal_features research_hypothesis_temporal_features_hypothesis_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_temporal_features
    ADD CONSTRAINT research_hypothesis_temporal_features_hypothesis_id_fkey FOREIGN KEY (hypothesis_id) REFERENCES public.research_hypotheses(hypothesis_id) ON DELETE CASCADE;


--
-- Name: research_hypothesis_variables research_hypothesis_variables_hypothesis_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_hypothesis_variables
    ADD CONSTRAINT research_hypothesis_variables_hypothesis_id_fkey FOREIGN KEY (hypothesis_id) REFERENCES public.research_hypotheses(hypothesis_id) ON DELETE CASCADE;


--
-- Name: research_knowledge_claims research_knowledge_claims_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_knowledge_claims
    ADD CONSTRAINT research_knowledge_claims_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id);


--
-- Name: research_operating_region_results research_operating_region_results_region_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_operating_region_results
    ADD CONSTRAINT research_operating_region_results_region_id_fkey FOREIGN KEY (operating_region_id) REFERENCES public.research_operating_regions(operating_region_id) ON DELETE CASCADE;


--
-- Name: research_operating_region_results research_operating_region_results_result_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_operating_region_results
    ADD CONSTRAINT research_operating_region_results_result_id_fkey FOREIGN KEY (result_id) REFERENCES public.research_experiment_results(result_id);


--
-- Name: research_operating_regions research_operating_regions_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_operating_regions
    ADD CONSTRAINT research_operating_regions_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id);


--
-- Name: research_project_members research_project_members_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_project_members
    ADD CONSTRAINT research_project_members_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id) ON DELETE CASCADE;


--
-- Name: research_rollback_drills research_rollback_drills_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_rollback_drills
    ADD CONSTRAINT research_rollback_drills_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id) ON DELETE CASCADE;


--
-- Name: research_shadow_recommendations research_shadow_recommendations_experiment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_shadow_recommendations
    ADD CONSTRAINT research_shadow_recommendations_experiment_id_fkey FOREIGN KEY (experiment_id) REFERENCES public.research_experiments(experiment_id);


--
-- Name: research_shadow_recommendations research_shadow_recommendations_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_shadow_recommendations
    ADD CONSTRAINT research_shadow_recommendations_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id);


--
-- Name: research_transfer_assessments research_transfer_assessments_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_transfer_assessments
    ADD CONSTRAINT research_transfer_assessments_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id) ON DELETE CASCADE;


--
-- Name: research_transfer_assessments research_transfer_assessments_source_operating_region_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_transfer_assessments
    ADD CONSTRAINT research_transfer_assessments_source_operating_region_id_fkey FOREIGN KEY (source_operating_region_id) REFERENCES public.research_operating_regions(operating_region_id);


--
-- Name: research_transfer_assessments research_transfer_assessments_source_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_transfer_assessments
    ADD CONSTRAINT research_transfer_assessments_source_project_id_fkey FOREIGN KEY (source_project_id) REFERENCES public.process_research_projects(project_id);


--
-- Name: research_validation_preregistrations research_validation_preregistrations_project_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.research_validation_preregistrations
    ADD CONSTRAINT research_validation_preregistrations_project_id_fkey FOREIGN KEY (project_id) REFERENCES public.process_research_projects(project_id) ON DELETE CASCADE;


--
-- Name: tooling_assembly_revisions tooling_assembly_revisions_tooling_assembly_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_assembly_revisions
    ADD CONSTRAINT tooling_assembly_revisions_tooling_assembly_id_fkey FOREIGN KEY (tooling_assembly_id) REFERENCES public.tooling_assemblies(tooling_assembly_id);


--
-- Name: tooling_installations tooling_installations_assembly_revision_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.tooling_installations
    ADD CONSTRAINT tooling_installations_assembly_revision_id_fkey FOREIGN KEY (assembly_revision_id) REFERENCES public.tooling_assembly_revisions(assembly_revision_id);


--
-- Name: user_sessions user_sessions_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_sessions
    ADD CONSTRAINT user_sessions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(user_id) ON DELETE CASCADE;


--
--



SELECT create_hypertable(
  'production_events', 'occurred_at',
  chunk_time_interval => INTERVAL '30 days',
  if_not_exists => TRUE, migrate_data => TRUE);

SELECT create_hypertable(
  'process_sample_frames', 'occurred_at',
  chunk_time_interval => INTERVAL '30 days',
  if_not_exists => TRUE, migrate_data => TRUE);

SELECT create_hypertable(
  'process_sample_values', 'occurred_at',
  chunk_time_interval => INTERVAL '30 days',
  if_not_exists => TRUE, migrate_data => TRUE);
