export type FactoryView = "feeding" | "machining" | "transfer" | "inspection" | "manual";

export type DataTone = "cyan" | "gold" | "mint";

export type FactoryDatum = {
  recordType: "equipment_state" | "machining_event" | "inspection_result" | "manual_inspection_record";
  recordName: string;
  eventType?: "cycle.started" | "cycle.completed";
  time: string;
  subject: string;
  source: string;
  context: string;
  data: string;
  correlationId?: string;
  sequence: number;
  tone: DataTone;
};

export type MachiningCycleData = {
  kind: "machining";
  started: FactoryDatum & { eventType: "cycle.started" };
  completed: FactoryDatum & { eventType: "cycle.completed" };
  settleAt: number;
};

export type StageRecordData = {
  kind: "record";
  active: FactoryDatum;
  settled: FactoryDatum;
  settleAt: number;
};

export type FactoryStageData = MachiningCycleData | StageRecordData;

export type FactoryStage = FactoryDatum & {
  code: string;
  view: FactoryView;
  capture: "automatic" | "hybrid";
  operation: string;
};

// Only the CNC machining boundary emits lifecycle events. The other stations
// contribute observable equipment state, inspection results, or durable
// records without inventing start/completion events for every physical step.
export const FACTORY_STAGE_DATA = [
  {
    kind: "record",
    active: {
      recordType: "equipment_state",
      recordName: "robot_machine_tending_state",
      time: "14:31:58.112",
      subject: "equipment/ROBOT-02",
      source: "ROBOT CONTROLLER + CNC INTERLOCK",
      context: "LOT-0716 · AL-6061 · LINE-CELL-A",
      data: "step=TRAY_PICK_TO_FIXED_FIXTURE · insertion=HORIZONTAL · gripper=PART_HELD · cnc_door=OPEN · spindle_rpm=0",
      sequence: 182340,
      tone: "cyan",
    },
    settled: {
      recordType: "equipment_state",
      recordName: "robot_machine_tending_state",
      time: "14:32:06.112",
      subject: "equipment/ROBOT-02",
      source: "ROBOT CONTROLLER + CNC INTERLOCK",
      context: "LOT-0716 · AL-6061 · LINE-CELL-A",
      data: "step=CNC_LOADED · gripper=RELEASED · fixture=CLAMPED · robot=HOME_CLEAR",
      sequence: 182341,
      tone: "mint",
    },
    settleAt: 0.98,
  },
  {
    kind: "machining",
    started: {
      recordType: "machining_event",
      recordName: "machining_cycle",
      eventType: "cycle.started",
      time: "14:32:08.429",
      subject: "equipment/CNC-07",
      source: "OPC UA / CNC",
      context: "LOT-0716 · ING-0718-0127 · RECIPE-R4",
      data: "operation=machining · program=O1207 · tool=T08",
      correlationId: "CYC-CNC-182342",
      sequence: 182342,
      tone: "gold",
    },
    completed: {
      recordType: "machining_event",
      recordName: "machining_cycle",
      eventType: "cycle.completed",
      time: "14:32:51.229",
      subject: "equipment/CNC-07",
      source: "OPC UA / CNC",
      context: "LOT-0716 · ING-0718-0127 · RECIPE-R4",
      data: "operation=machining · duration_ms=42800 · result=OK · program=O1207",
      correlationId: "CYC-CNC-182342",
      sequence: 182343,
      tone: "mint",
    },
    // Tool retracted and spindle stopped; the door opens before the table advances at 86%.
    settleAt: 0.72,
  },
  {
    kind: "record",
    active: {
      recordType: "equipment_state",
      recordName: "robot_motion_state",
      time: "14:32:52.803",
      subject: "equipment/ROBOT-02",
      source: "ROBOT CONTROLLER",
      context: "ING-0718-0127 · CELL-A · OP30",
      data: "step=FIXED_FIXTURE_TO_VISION · insertion=HORIZONTAL · speed_mm_s=620 · gripper=PART_HELD · spindle_safe=TRUE",
      sequence: 182344,
      tone: "cyan",
    },
    settled: {
      recordType: "equipment_state",
      recordName: "robot_motion_state",
      time: "14:32:56.203",
      subject: "equipment/ROBOT-02",
      source: "ROBOT CONTROLLER",
      context: "ING-0718-0127 · CELL-A · OP30",
      data: "step=HOME_WAITING · speed_mm_s=0 · gripper=PART_RELEASED · output_nest=OCCUPIED",
      sequence: 182345,
      tone: "mint",
    },
    settleAt: 0.94,
  },
  {
    kind: "record",
    active: {
      recordType: "equipment_state",
      recordName: "vision_acquisition_state",
      time: "14:32:56.506",
      subject: "equipment/VISION-03",
      source: "VISION PUSH",
      context: "ING-0718-0127 · RECIPE-V3.2 · 3 CAMERAS",
      data: "state=ACQUIRING · recipe=HOUSING_TOP_V3.2 · cameras=3",
      sequence: 182346,
      tone: "cyan",
    },
    settled: {
      recordType: "inspection_result",
      recordName: "vision_measurement",
      time: "14:32:56.792",
      subject: "equipment/VISION-03",
      source: "VISION PUSH",
      context: "ING-0718-0127 · RECIPE-V3.2 · 3 CAMERAS",
      data: "result=PASS · score=97.8 · hole_pitch_mm=32.047 · image_id=IMG-182347",
      sequence: 182347,
      tone: "mint",
    },
    settleAt: 0.72,
  },
  {
    kind: "record",
    active: {
      recordType: "manual_inspection_record",
      recordName: "manual_inspection_pending",
      time: "14:33:04.110",
      subject: "inspection/manual/MI-LOT-0716-001",
      source: "ROUGHNESS TESTER / QUALITY HMI",
      context: "ING-0718-0127 · LOT-0716 · ROUGHNESS-01 · QE-018",
      data: "inspection_status=MEASURING · characteristic=surface_roughness_ra_um · input_mode=INSTRUMENT",
      sequence: 182348,
      tone: "gold",
    },
    settled: {
      recordType: "manual_inspection_record",
      recordName: "manual_inspection_result",
      time: "14:33:18.642",
      subject: "inspection/manual/MI-LOT-0716-001",
      source: "ROUGHNESS TESTER / QUALITY HMI",
      context: "ING-0718-0127 · LOT-0716 · ROUGHNESS-01 · QE-018",
      data: "inspection_status=RECORDED · result=PASS · surface_roughness_ra_um=0.82 · upper_limit_ra_um=1.60 · inspector=QE-018 · instrument=ROUGHNESS-01 · calibration_status=VALID",
      sequence: 182349,
      tone: "mint",
    },
    settleAt: 0.82,
  },
] as const satisfies readonly FactoryStageData[];

const initialDatum = (index: number) => {
  const model = FACTORY_STAGE_DATA[index];
  return model.kind === "machining" ? model.started : model.active;
};

export const FACTORY_STAGES = [
  {
    code: "ROBOT-02 · LOAD",
    view: "feeding",
    capture: "automatic",
    operation: "robot_machine_load",
    ...initialDatum(0),
  },
  {
    code: "CNC-07",
    view: "machining",
    capture: "automatic",
    operation: "machining",
    ...initialDatum(1),
  },
  {
    code: "ROBOT-02 · UNLOAD",
    view: "transfer",
    capture: "automatic",
    operation: "robot_machine_unload",
    ...initialDatum(2),
  },
  {
    code: "VISION-03",
    view: "inspection",
    capture: "automatic",
    operation: "vision_inspection",
    ...initialDatum(3),
  },
  {
    code: "QA-HMI-01",
    view: "manual",
    capture: "hybrid",
    operation: "manual_inspection",
    ...initialDatum(4),
  },
] as const satisfies readonly FactoryStage[];

export const FACTORY_STAGE_MS = 3400;
