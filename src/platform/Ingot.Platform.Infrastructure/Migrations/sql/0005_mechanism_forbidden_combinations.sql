CREATE TABLE mechanism_claim_forbidden_combinations (
  combination_id uuid PRIMARY KEY,
  claim_id uuid NOT NULL,
  claim_version integer NOT NULL,
  name text NOT NULL,
  CONSTRAINT mechanism_claim_forbidden_combinations_name_check CHECK (length(btrim(name)) > 0),
  CONSTRAINT mechanism_claim_forbidden_combinations_claim_fk
    FOREIGN KEY (claim_id, claim_version)
    REFERENCES mechanism_claim_versions(claim_id, version) ON DELETE CASCADE,
  CONSTRAINT mechanism_claim_forbidden_combinations_name_unique
    UNIQUE (claim_id, claim_version, name)
);

CREATE TABLE mechanism_claim_forbidden_combination_factors (
  combination_id uuid NOT NULL,
  variable_code text NOT NULL,
  minimum double precision,
  maximum double precision,
  unit text NOT NULL,
  PRIMARY KEY (combination_id, variable_code),
  CONSTRAINT mechanism_claim_forbidden_combination_factors_combination_fk
    FOREIGN KEY (combination_id)
    REFERENCES mechanism_claim_forbidden_combinations(combination_id) ON DELETE CASCADE,
  CONSTRAINT mechanism_claim_forbidden_combination_factors_bounds_check
    CHECK ((minimum IS NOT NULL OR maximum IS NOT NULL) AND
           (minimum IS NULL OR maximum IS NULL OR minimum <= maximum))
);

CREATE INDEX mechanism_claim_forbidden_combinations_claim_idx
  ON mechanism_claim_forbidden_combinations(claim_id, claim_version);
