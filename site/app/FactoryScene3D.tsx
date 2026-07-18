"use client";

import { Canvas, useFrame, useThree } from "@react-three/fiber";
import { type MutableRefObject, type ReactNode, useEffect, useMemo, useRef } from "react";
import * as THREE from "three";
import {
  FACTORY_STAGE_DATA,
  FACTORY_STAGE_MS,
  FACTORY_STAGES,
  type FactoryView,
} from "./factoryStages";

export type { FactoryView } from "./factoryStages";

type FactoryScene3DProps = {
  activeStage: number;
  stageSettled: boolean;
  stageRevision: number;
  fallback?: ReactNode;
  paused: boolean;
  view: FactoryView;
};

const CAMERA_VIEWS: Record<
  FactoryView,
  { position: [number, number, number]; target: [number, number, number] }
> = {
  feeding: { position: [0.2, 3.45, 6.65], target: [-2.35, 1.32, -0.62] },
  machining: { position: [-4.7, 3.4, 7.2], target: [-3.55, 1.65, 0.15] },
  transfer: { position: [1.15, 3.35, 6.25], target: [-2.05, 1.38, -0.72] },
  inspection: { position: [6.9, 3.25, 5.5], target: [3.72, 1.5, 0.02] },
  manual: { position: [-6.1, 2.7, 6.45], target: [-3, 1.35, 2.08] },
};

const gold = "#e6a73a";
const goldBright = "#ffd06d";
const cyan = "#61d9d1";
const CELL_STATIONS = FACTORY_STAGES
  .filter((stage) => stage.view !== "manual")
  .map((stage) => stage.code);

type HmiRow = readonly [label: string, value: string, tone?: "normal" | "ok" | "warn"];

type HmiSpec = {
  title: string;
  subtitle: string;
  status: string;
  accent: string;
  rows: readonly HmiRow[];
  footer: string;
};

function createHmiTexture(spec: HmiSpec) {
  if (typeof document === "undefined") return new THREE.Texture();

  const canvas = document.createElement("canvas");
  canvas.width = 640;
  canvas.height = 800;
  const context = canvas.getContext("2d");
  if (!context) return new THREE.Texture();

  const background = context.createLinearGradient(0, 0, 0, canvas.height);
  background.addColorStop(0, "#10201f");
  background.addColorStop(1, "#07100f");
  context.fillStyle = background;
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.strokeStyle = "rgba(153, 232, 219, .16)";
  context.lineWidth = 2;
  for (let x = 0; x <= canvas.width; x += 40) {
    context.beginPath();
    context.moveTo(x, 0);
    context.lineTo(x, canvas.height);
    context.stroke();
  }
  for (let y = 0; y <= canvas.height; y += 40) {
    context.beginPath();
    context.moveTo(0, y);
    context.lineTo(canvas.width, y);
    context.stroke();
  }

  context.fillStyle = "rgba(255, 255, 255, .04)";
  context.fillRect(24, 24, canvas.width - 48, 116);
  context.fillStyle = spec.accent;
  context.fillRect(24, 24, 8, 116);
  context.font = "700 38px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";
  context.fillText(spec.title, 56, 72);
  context.font = "600 18px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";
  context.fillStyle = "#8da8a1";
  context.fillText(spec.subtitle, 56, 108);

  const statusWidth = Math.max(146, context.measureText(spec.status).width + 44);
  context.fillStyle = spec.accent;
  context.fillRect(canvas.width - statusWidth - 24, 44, statusWidth, 58);
  context.fillStyle = "#08110f";
  context.textAlign = "center";
  context.font = "800 20px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";
  context.fillText(spec.status, canvas.width - statusWidth / 2 - 24, 81);
  context.textAlign = "left";

  spec.rows.forEach(([label, value, tone], index) => {
    const y = 192 + index * 72;
    context.fillStyle = index % 2 === 0 ? "rgba(255,255,255,.035)" : "rgba(255,255,255,.018)";
    context.fillRect(24, y - 38, canvas.width - 48, 56);
    context.font = "600 17px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";
    context.fillStyle = "#6f8c85";
    context.fillText(label, 44, y - 2);
    context.textAlign = "right";
    context.font = "700 22px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";
    context.fillStyle = tone === "ok" ? "#7ff0b9" : tone === "warn" ? "#ffd477" : "#e8eee9";
    context.fillText(value, canvas.width - 44, y - 2);
    context.textAlign = "left";
  });

  context.fillStyle = "rgba(99, 217, 209, .12)";
  context.fillRect(24, canvas.height - 82, canvas.width - 48, 44);
  context.font = "600 16px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";
  context.fillStyle = "#8fc9c2";
  context.fillText(spec.footer, 44, canvas.height - 53);

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.minFilter = THREE.LinearFilter;
  texture.magFilter = THREE.LinearFilter;
  texture.anisotropy = 4;
  return texture;
}

function HmiDisplay({ height, spec, width }: { height: number; spec: HmiSpec; width: number }) {
  const texture = useMemo(() => createHmiTexture(spec), [spec]);

  useEffect(() => () => texture.dispose(), [texture]);

  return (
    <mesh>
      <planeGeometry args={[width, height]} />
      <meshBasicMaterial map={texture} toneMapped={false} />
    </mesh>
  );
}

function manualInspectionHmiSpec(phase: number): HmiSpec {
  if (phase === 0) {
    return {
      title: "MANUAL INSPECTION",
      subtitle: "SURFACE ROUGHNESS · ROUGHNESS-01",
      status: "PRESENT CARD",
      accent: "#f4b740",
      rows: [
        ["WORKPIECE", "ING-0718-0127"],
        ["LOT", "LOT-0716"],
        ["CHECK ITEM", "SURFACE ROUGHNESS Ra"],
        ["LIMIT", "Ra <= 1.60 um"],
        ["INSTRUMENT", "ROUGHNESS-01 · READY", "ok"],
        ["INSPECTOR", "PRESENT CARD", "warn"],
        ["MEASUREMENT", "WAITING", "warn"],
      ],
      footer: "STEP 1 / 4 · IDENTIFY INSPECTOR · MEASUREMENT NOT STARTED",
    };
  }
  if (phase === 1) {
    return {
      title: "MANUAL INSPECTION",
      subtitle: "SURFACE ROUGHNESS · QE-018",
      status: "MEASURE WORKPIECE",
      accent: "#f4b740",
      rows: [
        ["INSPECTOR", "QE-018 · IDENTIFIED", "ok"],
        ["WORKPIECE SCAN", "ING-0718-0127 · MATCH", "ok"],
        ["INSTRUMENT", "ROUGHNESS-01 · CONNECTED", "ok"],
        ["CALIBRATION", "VALID · 2026-12-31", "ok"],
        ["CHECK ITEM", "SURFACE ROUGHNESS Ra"],
        ["LIMIT", "Ra <= 1.60 um"],
        ["PROBE", "PLACE ON SURFACE", "warn"],
      ],
      footer: "STEP 2 / 4 · OPERATE TESTER · VALUE RETURNS FROM INSTRUMENT",
    };
  }
  if (phase === 2) {
    return {
      title: "MANUAL INSPECTION",
      subtitle: "SURFACE ROUGHNESS · RESULT RECEIVED",
      status: "CONFIRM RESULT",
      accent: "#f4b740",
      rows: [
        ["WORKPIECE / LOT", "ING-0718-0127 · LOT-0716", "ok"],
        ["MEASURED Ra", "0.82 um", "ok"],
        ["UPPER LIMIT", "1.60 um", "ok"],
        ["RESULT", "PASS", "ok"],
        ["INSTRUMENT", "ROUGHNESS-01", "ok"],
        ["INSPECTOR", "QE-018", "ok"],
        ["SAVE RESULT", "TOUCH TO SAVE", "warn"],
      ],
      footer: "STEP 3 / 4 · CONFIRM VALUE AND RESULT · NO MACHINE CONTROL",
    };
  }
  return {
    title: "MANUAL INSPECTION",
    subtitle: "INSPECTION RECORD · MI-LOT-0716-001",
    status: "RESULT SAVED",
    accent: "#49dfa0",
    rows: [
      ["WORKPIECE", "ING-0718-0127", "ok"],
      ["CHECK ITEM", "SURFACE ROUGHNESS Ra", "ok"],
      ["MEASUREMENT", "0.82 um", "ok"],
      ["RESULT", "PASS", "ok"],
      ["INSTRUMENT", "ROUGHNESS-01", "ok"],
      ["INSPECTOR", "QE-018", "ok"],
      ["CALIBRATION", "VALID", "ok"],
    ],
    footer: "STEP 4 / 4 · INSPECTION RESULT SAVED · PART HISTORY UPDATED",
  };
}

function ManualInspectionHmiDisplay({ height, process, width }: { height: number; process: ProcessRef; width: number }) {
  const material = useRef<THREE.MeshBasicMaterial>(null);
  const currentPhase = useRef(0);
  const initialTexture = useMemo(() => createHmiTexture(manualInspectionHmiSpec(0)), []);

  useFrame(() => {
    if (!material.current) return;
    const progress = process.current.progress;
    const phase = progress < 0.18 ? 0 : progress < 0.42 ? 1 : progress < 0.82 ? 2 : 3;
    if (phase === currentPhase.current) return;
    currentPhase.current = phase;
    const previousTexture = material.current.map;
    material.current.map = createHmiTexture(manualInspectionHmiSpec(phase));
    material.current.needsUpdate = true;
    previousTexture?.dispose();
  });

  useEffect(
    () => () => {
      material.current?.map?.dispose();
    },
    [],
  );

  return (
    <mesh>
      <planeGeometry args={[width, height]} />
      <meshBasicMaterial ref={material} map={initialTexture} toneMapped={false} />
    </mesh>
  );
}

type ProcessSnapshot = {
  progress: number;
  stage: number;
};

type ProcessRef = MutableRefObject<ProcessSnapshot>;

const clamp01 = (value: number) => Math.min(1, Math.max(0, value));
const smooth = (value: number) => {
  const t = clamp01(value);
  return t * t * (3 - 2 * t);
};
const windowed = (progress: number, start: number, end: number) =>
  smooth((progress - start) / (end - start));

const REVIEW_ARM_L1 = 0.36;
const REVIEW_ARM_L2 = 0.34;

function solveInspectionArmPose(down: number, forward: number) {
  const distance = THREE.MathUtils.clamp(
    Math.hypot(down, forward),
    Math.abs(REVIEW_ARM_L1 - REVIEW_ARM_L2) + 0.002,
    REVIEW_ARM_L1 + REVIEW_ARM_L2 - 0.012,
  );
  const aim = Math.atan2(forward, down);
  const shoulderOffset = Math.acos(THREE.MathUtils.clamp(
    (REVIEW_ARM_L1 ** 2 + distance ** 2 - REVIEW_ARM_L2 ** 2) /
      (2 * REVIEW_ARM_L1 * distance),
    -1,
    1,
  ));
  const elbow = Math.PI - Math.acos(THREE.MathUtils.clamp(
    (REVIEW_ARM_L1 ** 2 + REVIEW_ARM_L2 ** 2 - distance ** 2) /
      (2 * REVIEW_ARM_L1 * REVIEW_ARM_L2),
    -1,
    1,
  ));
  return { elbow, shoulder: aim - shoulderOffset };
}

// Contact points stop just in front of the reader and glass; the hand never enters either mesh.
const BADGE_READER_ARM_POSE = solveInspectionArmPose(0.32, 0.18);
const SAVE_BUTTON_ARM_POSE = solveInspectionArmPose(0.2, 0.56);

function useProcessClock(activeStage: number, stageRevision: number, paused: boolean, reducedMotion: boolean) {
  const process = useRef<ProcessSnapshot>({ progress: reducedMotion ? 0.5 : 0, stage: activeStage });
  const lastStage = useRef(activeStage);
  const lastRevision = useRef(stageRevision);
  const elapsed = useRef(0);

  useFrame((_, delta) => {
    if (lastStage.current !== activeStage || lastRevision.current !== stageRevision) {
      lastStage.current = activeStage;
      lastRevision.current = stageRevision;
      elapsed.current = 0;
    }
    if (!paused && !reducedMotion) elapsed.current += delta;
    process.current.stage = activeStage;
    process.current.progress = reducedMotion
      ? 0.5
      : clamp01(elapsed.current / (FACTORY_STAGE_MS / 1000));
  });

  return process;
}

function CameraRig({ view, reducedMotion }: { view: FactoryView; reducedMotion: boolean }) {
  const camera = useThree((state) => state.camera);
  const lookAt = useRef(new THREE.Vector3(...CAMERA_VIEWS.feeding.target));
  const destination = useMemo(
    () => new THREE.Vector3(...CAMERA_VIEWS[view].position),
    [view],
  );
  const destinationTarget = useMemo(
    () => new THREE.Vector3(...CAMERA_VIEWS[view].target),
    [view],
  );

  useEffect(() => {
    if (!reducedMotion) return;
    camera.position.copy(destination);
    lookAt.current.copy(destinationTarget);
    camera.lookAt(lookAt.current);
    camera.updateProjectionMatrix();
  }, [camera, destination, destinationTarget, reducedMotion]);

  useFrame((_, delta) => {
    if (reducedMotion) return;
    const positionEase = 1 - Math.exp(-delta * 1.65);
    const targetEase = 1 - Math.exp(-delta * 2.1);
    camera.position.lerp(destination, positionEase);
    lookAt.current.lerp(destinationTarget, targetEase);
    camera.lookAt(lookAt.current);
  });

  return null;
}

function FactoryShell() {
  const ceilingLights = [-10, -5, 0, 5, 10].flatMap((x) =>
    [-2.65, 2.65].map((z) => [x, z] as const),
  );
  const roofBeams = [-8, -4, 0, 4, 8];

  return (
    <group>
      <mesh rotation={[-Math.PI / 2, 0, 0]} receiveShadow>
        <planeGeometry args={[30, 18]} />
        <meshStandardMaterial color="#8e9692" roughness={0.84} metalness={0.08} />
      </mesh>
      <gridHelper
        args={[30, 30, "#c7ceca", "#737c77"]}
        position={[0, 0.012, 0]}
      />
      <mesh position={[0, 3.3, -5.8]} receiveShadow>
        <boxGeometry args={[30, 6.6, 0.22]} />
        <meshStandardMaterial color="#c5ccc8" roughness={0.76} metalness={0.08} />
      </mesh>
      <mesh position={[-14.8, 3.3, 0]} receiveShadow>
        <boxGeometry args={[0.22, 6.6, 12]} />
        <meshStandardMaterial color="#b9c1bd" roughness={0.82} metalness={0.06} />
      </mesh>
      <mesh position={[14.8, 3.3, 0]} receiveShadow>
        <boxGeometry args={[0.22, 6.6, 12]} />
        <meshStandardMaterial color="#b9c1bd" roughness={0.82} metalness={0.06} />
      </mesh>

      {roofBeams.map((x) => (
        <group key={x}>
          <mesh position={[x, 6.25, 0]}>
            <boxGeometry args={[0.18, 0.22, 12]} />
            <meshStandardMaterial color="#6d7872" roughness={0.52} metalness={0.66} />
          </mesh>
          <mesh position={[x, 3.15, -5.45]}>
            <boxGeometry args={[0.28, 6.25, 0.32]} />
            <meshStandardMaterial color="#75817a" roughness={0.6} metalness={0.62} />
          </mesh>
        </group>
      ))}

      {ceilingLights.map(([x, z], index) => (
        <group key={`${x}-${z}`} position={[x, 5.9, z]}>
          <mesh>
            <boxGeometry args={[3.25, 0.08, 0.5]} />
            <meshStandardMaterial
              color="#f8fbf9"
              emissive="#f4fff9"
              emissiveIntensity={3.6}
            />
          </mesh>
          <pointLight
            color="#f2fff9"
            intensity={12}
            distance={7.5}
            decay={2}
            castShadow={index === 4}
          />
        </group>
      ))}

      {[-9.4, -3.2, 3.2, 9.4].map((x) => (
        <group key={x} position={[x, 3.55, -5.62]}>
          <mesh>
            <boxGeometry args={[3.8, 1.65, 0.06]} />
            <meshPhysicalMaterial
              color="#163a43"
              emissive="#0d2930"
              emissiveIntensity={0.55}
              roughness={0.28}
              metalness={0.18}
              transparent
              opacity={0.72}
            />
          </mesh>
          <mesh position={[0, -1.05, 0.04]}>
            <boxGeometry args={[3.8, 0.04, 0.04]} />
            <meshStandardMaterial color={cyan} emissive={cyan} emissiveIntensity={1.8} />
          </mesh>
        </group>
      ))}

      <mesh position={[0, 0.025, 2.35]} rotation={[-Math.PI / 2, 0, 0]}>
        <planeGeometry args={[25, 0.12]} />
        <meshStandardMaterial color={gold} emissive={gold} emissiveIntensity={1.2} />
      </mesh>
      <mesh position={[0, 0.026, -2.25]} rotation={[-Math.PI / 2, 0, 0]}>
        <planeGeometry args={[25, 0.08]} />
        <meshStandardMaterial color="#b49856" emissive="#6e5627" emissiveIntensity={0.6} />
      </mesh>
    </group>
  );
}

function StatusTower({ active, position = [0.72, 2.72, 0] }: { active: boolean; position?: [number, number, number] }) {
  return (
    <group position={position}>
      <mesh>
        <cylinderGeometry args={[0.025, 0.025, 0.45, 10]} />
        <meshStandardMaterial color="#59635d" metalness={0.78} roughness={0.3} />
      </mesh>
      <mesh position={[0, 0.27, 0]}>
        <cylinderGeometry args={[0.075, 0.075, 0.12, 14]} />
        <meshStandardMaterial
          color={active ? "#7af3bf" : "#4faa78"}
          emissive={active ? "#4ff3a4" : "#1b6d48"}
          emissiveIntensity={active ? 3.2 : 1.15}
        />
      </mesh>
      <mesh position={[0, 0.4, 0]}>
        <cylinderGeometry args={[0.075, 0.075, 0.1, 14]} />
        <meshStandardMaterial color="#725e32" emissive="#2b210d" emissiveIntensity={0.22} />
      </mesh>
      <mesh position={[0, 0.51, 0]}>
        <cylinderGeometry args={[0.075, 0.075, 0.1, 14]} />
        <meshStandardMaterial color="#572d2d" emissive="#210808" emissiveIntensity={0.18} />
      </mesh>
    </group>
  );
}

function MachinePad({ active }: { active: boolean }) {
  return (
    <group>
      <mesh position={[0, 0.12, 0]} receiveShadow>
        <boxGeometry args={[2.65, 0.22, 2.65]} />
        <meshStandardMaterial color="#1d2521" metalness={0.72} roughness={0.46} />
      </mesh>
      <mesh position={[0, 0.245, 0]}>
        <boxGeometry args={[2.42, 0.025, 2.42]} />
        <meshStandardMaterial
          color={active ? gold : "#4a4f48"}
          emissive={active ? gold : "#111411"}
          emissiveIntensity={active ? 1.55 : 0.15}
        />
      </mesh>
      {active && <pointLight position={[0, 1.2, 1.25]} color={goldBright} intensity={8} distance={4} />}
    </group>
  );
}

function FencePanel({ axis, center, length }: { axis: "x" | "z"; center: [number, number, number]; length: number }) {
  const panelSize: [number, number, number] = axis === "x" ? [length, 2.05, 0.035] : [0.035, 2.05, length];
  const railSize: [number, number, number] = axis === "x" ? [length, 0.055, 0.055] : [0.055, 0.055, length];

  return (
    <group position={center}>
      <mesh>
        <boxGeometry args={panelSize} />
        <meshPhysicalMaterial
          color="#79a7a2"
          roughness={0.12}
          metalness={0.08}
          transparent
          opacity={0.085}
          depthWrite={false}
        />
      </mesh>
      {[-1.02, 1.02].map((y) => (
        <mesh key={y} position={[0, y, 0]}>
          <boxGeometry args={railSize} />
          <meshStandardMaterial color="#c99a3f" metalness={0.72} roughness={0.32} />
        </mesh>
      ))}
      {Array.from({ length: Math.max(2, Math.ceil(length / 2)) + 1 }, (_, index) => {
        const offset = -length / 2 + (index * length) / Math.max(2, Math.ceil(length / 2));
        return (
          <mesh key={offset} position={axis === "x" ? [offset, 0, 0] : [0, 0, offset]}>
            <boxGeometry args={[0.055, 2.1, 0.055]} />
            <meshStandardMaterial color="#c99a3f" metalness={0.72} roughness={0.32} />
          </mesh>
        );
      })}
    </group>
  );
}

function InterlockedGate({ x, z }: { x: number; z: number }) {
  return (
    <group position={[x, 1.14, z]}>
      <mesh>
        <boxGeometry args={[1.05, 2.1, 0.04]} />
        <meshPhysicalMaterial
          color="#7da8a4"
          transparent
          opacity={0.1}
          depthWrite={false}
          roughness={0.14}
        />
      </mesh>
      {[-0.54, 0.54].map((side) => (
        <mesh key={side} position={[side, 0, 0]}>
          <boxGeometry args={[0.065, 2.18, 0.065]} />
          <meshStandardMaterial color="#d1a348" metalness={0.7} roughness={0.3} />
        </mesh>
      ))}
      <mesh position={[0.38, 0.02, 0.08]}>
        <boxGeometry args={[0.12, 0.24, 0.08]} />
        <meshStandardMaterial color="#1b2822" metalness={0.68} roughness={0.36} />
      </mesh>
      <mesh position={[0.38, 0.08, 0.13]}>
        <boxGeometry args={[0.035, 0.06, 0.025]} />
        <meshStandardMaterial color="#53dda0" emissive="#2cb874" emissiveIntensity={1.8} />
      </mesh>
    </group>
  );
}

function ConveyorLightCurtain({ x }: { x: number }) {
  return (
    <group position={[x, 0, 0]}>
      {[-0.76, 0.76].map((z) => (
        <group key={z} position={[0, 1.02, z]}>
          <mesh castShadow>
            <boxGeometry args={[0.1, 1.82, 0.1]} />
            <meshStandardMaterial color="#343e39" metalness={0.74} roughness={0.32} />
          </mesh>
          {[0.18, 0.52, 0.86, 1.2, 1.54].map((y) => (
            <mesh key={y} position={[0, y - 0.91, z > 0 ? -0.06 : 0.06]}>
              <boxGeometry args={[0.025, 0.025, 0.025]} />
              <meshStandardMaterial color="#63d9d1" emissive="#35bfb6" emissiveIntensity={2.2} />
            </mesh>
          ))}
        </group>
      ))}
      {[0.29, 0.63, 0.97, 1.31, 1.65].map((y) => (
        <mesh key={y} position={[0, y, 0]}>
          <boxGeometry args={[0.018, 0.018, 1.42]} />
          <meshBasicMaterial color="#79e7de" transparent opacity={0.34} />
        </mesh>
      ))}
    </group>
  );
}

function SafetyPerimeter() {
  return (
    <group>
      {/* The CNC and its tending robot form one interlocked cell; the operator aisle stays outside. */}
      <FencePanel axis="x" center={[-2.2, 1.14, 1.15]} length={5.9} />
      <FencePanel axis="x" center={[-3.68, 1.14, -3.62]} length={2.9} />
      <FencePanel axis="x" center={[-0.72, 1.14, -3.62]} length={1.9} />
      {[-5.15, 0.75].flatMap((x) => [
        <FencePanel key={`${x}-rear`} axis="z" center={[x, 1.14, -2.28]} length={2.68} />,
        <FencePanel key={`${x}-front`} axis="z" center={[x, 1.14, 0.76]} length={0.78} />,
      ])}
      <InterlockedGate x={-2.2} z={-3.62} />
      <ConveyorLightCurtain x={-5.15} />
      <ConveyorLightCurtain x={0.75} />

      {[-4.88, 0.48].map((x) => (
        <group key={x} position={[x, 1.1, 1.22]}>
          <mesh>
            <boxGeometry args={[0.18, 0.38, 0.14]} />
            <meshStandardMaterial color="#25312b" metalness={0.72} roughness={0.34} />
          </mesh>
          <mesh position={[0, 0.07, 0.09]} rotation={[Math.PI / 2, 0, 0]}>
            <cylinderGeometry args={[0.065, 0.065, 0.06, 18]} />
            <meshStandardMaterial color="#c83b31" emissive="#69130f" emissiveIntensity={0.7} />
          </mesh>
        </group>
      ))}
    </group>
  );
}

function CellOverviewConsole({ activeStage, stageSettled, process }: { activeStage: number; stageSettled: boolean; process: ProcessRef }) {
  const uploadBar = useRef<THREE.Group>(null);
  const uploadMaterial = useRef<THREE.MeshStandardMaterial>(null);
  const badgeReaderMaterial = useRef<THREE.MeshStandardMaterial>(null);
  const scannerBeam = useRef<THREE.MeshBasicMaterial>(null);
  const activeCode = FACTORY_STAGES[activeStage]?.code;
  const activeData = FACTORY_STAGE_DATA[activeStage] ?? FACTORY_STAGE_DATA[0];
  const manualInspectionActive = FACTORY_STAGES[activeStage]?.view === "manual";
  const consoleScreen = useMemo<HmiSpec>(
    () => ({
      title: "CELL-A LOCAL HMI",
      subtitle: "BATCH AUTO · SINGLE-PART TRACE",
      status: "GUARDS LOCKED",
      accent: "#49dfa0",
      rows: CELL_STATIONS.map((name) => [
        name,
        name === activeCode
          ? activeData.kind === "machining"
            ? stageSettled ? "MACHINING ENDED" : "MACHINING"
            : stageSettled ? "DATA RECORDED" : "DATA UPDATING"
          : "AUTO READY",
        "ok",
      ] as HmiRow),
      footer: "LOT-0716  ·  126 / 240  ·  TIME-COMPRESSED DATA VIEW  ·  NO ALARMS",
    }),
    [activeCode, activeData.kind, stageSettled],
  );

  useFrame(() => {
    const progress = manualInspectionActive ? process.current.progress : 0;
    if (badgeReaderMaterial.current) {
      const reading = manualInspectionActive && progress >= 0.08 && progress < 0.3;
      badgeReaderMaterial.current.color.set(reading ? "#69efe0" : "#40544d");
      badgeReaderMaterial.current.emissive.set(reading ? "#21bdae" : "#10211c");
      badgeReaderMaterial.current.emissiveIntensity = reading ? 2.3 : 0.35;
    }
    if (scannerBeam.current) {
      scannerBeam.current.opacity = manualInspectionActive && progress >= 0.28 && progress < 0.44 ? 0.68 : 0.08;
    }
    if (!uploadBar.current || !uploadMaterial.current) return;
    const upload = manualInspectionActive
      ? THREE.MathUtils.lerp(0.04, 1, windowed(progress, 0.34, 0.82))
      : 0.04;
    uploadBar.current.visible = manualInspectionActive;
    uploadBar.current.scale.x = upload;
    const persisted = progress >= 0.82;
    uploadMaterial.current.color.set(persisted ? "#49dfa0" : "#f4b740");
    uploadMaterial.current.emissive.set(persisted ? "#238b61" : "#805717");
    uploadMaterial.current.emissiveIntensity = 1.35;
  });

  return (
    <group position={[-3, 0, 1.9]}>
      <mesh position={[0, 0.66, 0]} castShadow>
        <boxGeometry args={[0.16, 1.28, 0.16]} />
        <meshStandardMaterial color="#444f49" metalness={0.84} roughness={0.3} />
      </mesh>
      <mesh position={[0, 1.02, 0.25]} castShadow>
        <boxGeometry args={[1.08, 0.08, 0.5]} />
        <meshStandardMaterial color="#39443f" metalness={0.72} roughness={0.34} />
      </mesh>
      <group position={[0, 1.43, 0.2]} rotation={[-0.08, 0, 0]}>
        <mesh position={[0, 0, -0.07]} castShadow>
          <boxGeometry args={[1.04, 0.82, 0.16]} />
          <meshStandardMaterial color="#141b18" metalness={0.76} roughness={0.36} />
        </mesh>
        {manualInspectionActive
          ? <ManualInspectionHmiDisplay width={0.9} height={0.66} process={process} />
          : <HmiDisplay width={0.9} height={0.66} spec={consoleScreen} />}
        <mesh position={[0, -0.355, 0.018]} visible={manualInspectionActive}>
          <boxGeometry args={[0.82, 0.026, 0.018]} />
          <meshStandardMaterial color="#202c28" metalness={0.45} roughness={0.45} />
        </mesh>
        <group ref={uploadBar} position={[-0.41, -0.355, 0.034]} visible={manualInspectionActive}>
          <mesh position={[0.41, 0, 0]}>
            <boxGeometry args={[0.82, 0.018, 0.012]} />
            <meshStandardMaterial ref={uploadMaterial} color="#f4b740" emissive="#805717" />
          </mesh>
        </group>
        <group position={[0.36, -0.48, 0.035]} visible={manualInspectionActive}>
          <mesh>
            <boxGeometry args={[0.18, 0.12, 0.035]} />
            <meshStandardMaterial color="#25332e" metalness={0.42} roughness={0.38} />
          </mesh>
          <mesh position={[0, 0, 0.021]}>
            <boxGeometry args={[0.12, 0.025, 0.01]} />
            <meshStandardMaterial color="#f4b740" emissive="#8a5c13" emissiveIntensity={1.2} />
          </mesh>
        </group>
        <mesh position={[-0.4, -0.5, 0.03]} rotation={[Math.PI / 2, 0, 0]}>
          <cylinderGeometry args={[0.075, 0.075, 0.06, 18]} />
          <meshStandardMaterial color="#c83d32" emissive="#70140f" emissiveIntensity={0.7} />
        </mesh>
        {[-0.12, 0.08, 0.28].map((x, index) => (
          <mesh key={x} position={[x, -0.5, 0.02]} rotation={[Math.PI / 2, 0, 0]}>
            <cylinderGeometry args={[0.035, 0.035, 0.03, 14]} />
            <meshStandardMaterial color={index === 0 ? "#50c77d" : index === 1 ? "#d2a641" : "#6f7b74"} />
          </mesh>
        ))}
      </group>
      <group position={[0.38, 1.11, 0.48]} rotation={[-0.22, 0, 0]} visible={manualInspectionActive}>
        <mesh castShadow>
          <boxGeometry args={[0.24, 0.055, 0.2]} />
          <meshStandardMaterial color="#1c2723" metalness={0.58} roughness={0.38} />
        </mesh>
        <mesh position={[0, 0.034, 0]}>
          <boxGeometry args={[0.18, 0.018, 0.13]} />
          <meshStandardMaterial ref={badgeReaderMaterial} color="#40544d" emissive="#10211c" />
        </mesh>
        <mesh position={[0, 0.047, -0.03]}>
          <torusGeometry args={[0.034, 0.006, 8, 18, Math.PI * 1.6]} />
          <meshBasicMaterial color="#d9fff8" />
        </mesh>
      </group>
      <group position={[-0.35, 1.12, 0.43]} visible={manualInspectionActive}>
        <mesh position={[0, 0.08, 0]} rotation={[0.18, 0, 0]} castShadow>
          <boxGeometry args={[0.18, 0.16, 0.16]} />
          <meshStandardMaterial color="#202b27" metalness={0.6} roughness={0.36} />
        </mesh>
        <mesh position={[0, 0.085, -0.09]} rotation={[0.18, 0, 0]}>
          <boxGeometry args={[0.12, 0.045, 0.018]} />
          <meshStandardMaterial color="#8d342f" emissive="#4d100d" emissiveIntensity={0.7} />
        </mesh>
        <mesh position={[0, -0.025, -0.13]} rotation={[-Math.PI / 2, 0, 0]}>
          <planeGeometry args={[0.19, 0.36]} />
          <meshBasicMaterial ref={scannerBeam} color="#ff5b4f" transparent opacity={0.08} depthWrite={false} />
        </mesh>
        <mesh position={[0, -0.04, 0.08]}>
          <boxGeometry args={[0.32, 0.035, 0.25]} />
          <meshStandardMaterial color="#53615b" metalness={0.42} roughness={0.48} />
        </mesh>
        <mesh position={[0, -0.016, 0.075]}>
          <boxGeometry args={[0.17, 0.012, 0.12]} />
          <meshStandardMaterial color="#d7ded9" roughness={0.62} />
        </mesh>
      </group>
    </group>
  );
}

function CellTechnician({ active, process }: { active: boolean; process: ProcessRef }) {
  const technician = useRef<THREE.Group>(null);
  const torso = useRef<THREE.Mesh>(null);
  const head = useRef<THREE.Group>(null);
  const leftUpperArm = useRef<THREE.Group>(null);
  const rightUpperArm = useRef<THREE.Group>(null);
  const rightForearm = useRef<THREE.Group>(null);

  useFrame((state) => {
    const progress = active ? process.current.progress : 0;
    const badgeTap = windowed(progress, 0.08, 0.18) * (1 - windowed(progress, 0.25, 0.34));
    const saveTap = windowed(progress, 0.5, 0.62) * (1 - windowed(progress, 0.86, 0.96));
    const reach = Math.max(badgeTap, saveTap);
    const breath = Math.sin(state.clock.elapsedTime * 1.45);
    if (technician.current) {
      technician.current.visible = active;
      technician.current.position.y = active ? breath * 0.006 : 0;
      technician.current.rotation.z = active ? Math.sin(state.clock.elapsedTime * 0.72) * 0.009 : 0;
    }
    if (torso.current) torso.current.scale.y = 1 + breath * 0.004;
    if (head.current) {
      head.current.rotation.x = active ? 0.05 + reach * 0.08 : 0;
      head.current.rotation.y = active ? -0.04 + Math.sin(state.clock.elapsedTime * 0.55) * 0.025 : 0;
    }
    if (rightUpperArm.current) {
      rightUpperArm.current.rotation.x =
        0.08 * (1 - reach) + BADGE_READER_ARM_POSE.shoulder * badgeTap + SAVE_BUTTON_ARM_POSE.shoulder * saveTap;
      rightUpperArm.current.rotation.z =
        0.12 * (1 - reach) + 0.2 * badgeTap + 0.025 * saveTap;
    }
    if (rightForearm.current) {
      rightForearm.current.rotation.x =
        0.08 * (1 - reach) + BADGE_READER_ARM_POSE.elbow * badgeTap + SAVE_BUTTON_ARM_POSE.elbow * saveTap;
    }
    if (leftUpperArm.current) {
      leftUpperArm.current.rotation.x = 0.05 + breath * 0.025;
      leftUpperArm.current.rotation.z = -0.13;
    }
  });

  return (
    <group ref={technician} position={[-3, 0, 2.78]} visible={active}>
      {/* Articulated legs with knees and low-profile safety shoes. */}
      {[-0.12, 0.12].map((x) => (
        <group key={x}>
          <mesh position={[x, 0.67, 0]} castShadow>
            <cylinderGeometry args={[0.09, 0.105, 0.46, 18]} />
            <meshStandardMaterial color="#243b4a" roughness={0.7} />
          </mesh>
          <mesh position={[x, 0.43, 0]} castShadow>
            <sphereGeometry args={[0.092, 16, 12]} />
            <meshStandardMaterial color="#213642" roughness={0.72} />
          </mesh>
          <mesh position={[x, 0.23, 0]} castShadow>
            <cylinderGeometry args={[0.075, 0.088, 0.36, 16]} />
            <meshStandardMaterial color="#263e4d" roughness={0.7} />
          </mesh>
          <mesh position={[x, 0.065, -0.055]} castShadow>
            <boxGeometry args={[0.2, 0.13, 0.34]} />
            <meshStandardMaterial color="#121817" roughness={0.54} />
          </mesh>
          <mesh position={[x, 0.116, -0.17]}>
            <boxGeometry args={[0.18, 0.025, 0.08]} />
            <meshStandardMaterial color="#4b5752" metalness={0.55} roughness={0.38} />
          </mesh>
        </group>
      ))}

      <mesh position={[0, 0.91, 0]} castShadow>
        <boxGeometry args={[0.43, 0.24, 0.27]} />
        <meshStandardMaterial color="#203845" roughness={0.66} />
      </mesh>
      <mesh ref={torso} position={[0, 1.22, 0]} castShadow>
        <cylinderGeometry args={[0.245, 0.31, 0.62, 24]} />
        <meshStandardMaterial color="#1f4354" roughness={0.56} />
      </mesh>
      <mesh position={[0, 1.22, -0.252]}>
        <boxGeometry args={[0.47, 0.075, 0.025]} />
        <meshStandardMaterial color="#e0bd55" emissive="#705817" emissiveIntensity={0.32} />
      </mesh>
      <mesh position={[0, 1.07, -0.255]}>
        <boxGeometry args={[0.43, 0.018, 0.018]} />
        <meshStandardMaterial color="#89a4aa" metalness={0.35} roughness={0.42} />
      </mesh>
      <mesh position={[0, 1.31, -0.258]}>
        <boxGeometry args={[0.018, 0.36, 0.018]} />
        <meshStandardMaterial color="#b9cbc9" metalness={0.3} roughness={0.4} />
      </mesh>
      <mesh position={[-0.15, 1.08, -0.267]}>
        <boxGeometry args={[0.16, 0.12, 0.016]} />
        <meshStandardMaterial color="#17303b" roughness={0.62} />
      </mesh>

      {/* Left arm hangs naturally; right arm reaches the inspection HMI in two joints. */}
      <group ref={leftUpperArm} position={[-0.31, 1.43, 0]}>
        <mesh position={[0, -0.17, 0]} castShadow>
          <capsuleGeometry args={[0.068, 0.25, 6, 12]} />
          <meshStandardMaterial color="#234858" roughness={0.58} />
        </mesh>
        <mesh position={[0, -0.36, 0]} castShadow>
          <sphereGeometry args={[0.07, 14, 10]} />
          <meshStandardMaterial color="#1c3c4a" roughness={0.6} />
        </mesh>
        <mesh position={[0, -0.52, 0]} castShadow>
          <capsuleGeometry args={[0.058, 0.22, 6, 12]} />
          <meshStandardMaterial color="#285063" roughness={0.58} />
        </mesh>
        <mesh position={[0, -0.69, -0.005]} castShadow>
          <sphereGeometry args={[0.067, 14, 10]} />
          <meshStandardMaterial color="#bd9273" roughness={0.7} />
        </mesh>
      </group>
      <group ref={rightUpperArm} position={[0.31, 1.43, 0]}>
        <mesh position={[0, -0.17, 0]} castShadow>
          <capsuleGeometry args={[0.068, 0.25, 6, 12]} />
          <meshStandardMaterial color="#234858" roughness={0.58} />
        </mesh>
        <mesh position={[0, -0.36, 0]} castShadow>
          <sphereGeometry args={[0.071, 14, 10]} />
          <meshStandardMaterial color="#1c3c4a" roughness={0.6} />
        </mesh>
        <group ref={rightForearm} position={[0, -0.36, 0]}>
          <mesh position={[0, -0.16, 0]} castShadow>
            <capsuleGeometry args={[0.058, 0.22, 6, 12]} />
            <meshStandardMaterial color="#285063" roughness={0.58} />
          </mesh>
          <mesh position={[0, -0.34, -0.005]} castShadow>
            <sphereGeometry args={[0.068, 14, 10]} />
            <meshStandardMaterial color="#bd9273" roughness={0.7} />
          </mesh>
          <mesh position={[0, -0.39, -0.055]} rotation={[Math.PI / 2, 0, 0]}>
            <capsuleGeometry args={[0.018, 0.075, 4, 8]} />
            <meshStandardMaterial color="#bd9273" roughness={0.7} />
          </mesh>
        </group>
      </group>

      <mesh position={[0, 1.53, 0]} castShadow>
        <cylinderGeometry args={[0.075, 0.085, 0.13, 16]} />
        <meshStandardMaterial color="#b88b6e" roughness={0.72} />
      </mesh>
      <group ref={head} position={[0, 1.69, 0]}>
        <mesh scale={[0.92, 1.04, 0.92]} castShadow>
          <sphereGeometry args={[0.17, 26, 20]} />
          <meshStandardMaterial color="#bd9273" roughness={0.68} />
        </mesh>
        <mesh position={[0, 0.095, 0.045]} scale={[1.02, 0.54, 1.02]}>
          <sphereGeometry args={[0.174, 24, 16]} />
          <meshStandardMaterial color="#2a241f" roughness={0.82} />
        </mesh>
        {[-0.065, 0.065].map((x) => (
          <group key={x} position={[x, 0.025, -0.158]}>
            <mesh>
              <sphereGeometry args={[0.016, 12, 8]} />
              <meshStandardMaterial color="#2a211d" roughness={0.7} />
            </mesh>
            <mesh position={[0, 0, -0.012]}>
              <boxGeometry args={[0.096, 0.052, 0.014]} />
              <meshPhysicalMaterial color="#a6e1e2" transparent opacity={0.46} roughness={0.08} />
            </mesh>
          </group>
        ))}
        <mesh position={[0, 0.016, -0.181]} rotation={[-Math.PI / 2, 0, 0]}>
          <coneGeometry args={[0.024, 0.065, 12]} />
          <meshStandardMaterial color="#b4876b" roughness={0.7} />
        </mesh>
        <mesh position={[0, -0.065, -0.166]}>
          <boxGeometry args={[0.065, 0.012, 0.012]} />
          <meshStandardMaterial color="#754c43" roughness={0.78} />
        </mesh>
        {[-0.174, 0.174].map((x) => (
          <group key={x} position={[x, 0.01, 0]}>
            <mesh>
              <sphereGeometry args={[0.036, 12, 9]} />
              <meshStandardMaterial color="#b88b6e" roughness={0.72} />
            </mesh>
            <mesh position={[x < 0 ? -0.01 : 0.01, 0, 0]}>
              <sphereGeometry args={[0.017, 10, 8]} />
              <meshStandardMaterial color="#e4a93f" emissive="#7a4a0a" emissiveIntensity={0.25} />
            </mesh>
          </group>
        ))}
      </group>

      <mesh position={[0, 1.35, -0.275]}>
        <torusGeometry args={[0.13, 0.008, 8, 24, Math.PI]} />
        <meshStandardMaterial color="#d6ad42" roughness={0.5} />
      </mesh>
      <group position={[0.11, 1.31, -0.287]} rotation={[-0.08, 0, 0]}>
        <mesh>
          <boxGeometry args={[0.14, 0.19, 0.018]} />
          <meshStandardMaterial color="#d9e4df" roughness={0.52} />
        </mesh>
        <mesh position={[0, -0.045, -0.012]}>
          <boxGeometry args={[0.09, 0.018, 0.008]} />
          <meshStandardMaterial color="#49dfa0" emissive="#21744f" emissiveIntensity={0.7} />
        </mesh>
      </group>
    </group>
  );
}

function RawBlankMagazine({ active }: { active: boolean }) {
  return (
    <group position={[-0.55, 0, -1.65]}>
      <mesh position={[0, 0.12, 0]} receiveShadow>
        <boxGeometry args={[1.25, 0.22, 1.05]} />
        <meshStandardMaterial color="#202823" metalness={0.76} roughness={0.4} />
      </mesh>
      {[-0.52, 0.52].flatMap((x) =>
        [-0.42, 0.42].map((z) => (
          <mesh key={`${x}-${z}`} position={[x, 0.88, z]} castShadow>
            <boxGeometry args={[0.08, 1.48, 0.08]} />
            <meshStandardMaterial color="#59655f" metalness={0.84} roughness={0.3} />
          </mesh>
        )),
      )}
      {[0.42, 0.68, 0.94, 1.2].map((y, index) => (
        <group key={y} position={[0, y, -0.06]}>
          <mesh castShadow>
            <boxGeometry args={[0.96, 0.06, 0.72]} />
            <meshStandardMaterial color="#48544e" metalness={0.86} roughness={0.28} />
          </mesh>
          {index > 1 && (
            <mesh position={[0, 0.07, 0]} castShadow>
              <boxGeometry args={[0.32, 0.12, 0.26]} />
              <meshStandardMaterial color="#aeb8b0" metalness={0.86} roughness={0.2} />
            </mesh>
          )}
        </group>
      ))}
      <mesh position={[0, 1.64, 0]} castShadow>
        <boxGeometry args={[1.18, 0.12, 0.94]} />
        <meshStandardMaterial color="#2d3732" metalness={0.8} roughness={0.34} />
      </mesh>
      <mesh position={[0.56, 1.48, 0.34]}>
        <boxGeometry args={[0.12, 0.34, 0.12]} />
        <meshStandardMaterial color={gold} emissive={active ? gold : "#2b1d0b"} emissiveIntensity={active ? 1.3 : 0.2} />
      </mesh>
      <StatusTower active={active} position={[0.54, 1.78, -0.34]} />
    </group>
  );
}

function CncMachine({ activeStage, eventCompleted, process }: { activeStage: number; eventCompleted: boolean; process: ProcessRef }) {
  const door = useRef<THREE.Group>(null);
  const spindle = useRef<THREE.Group>(null);
  const active = activeStage === 1;
  const cncScreen = useMemo<HmiSpec>(
    () => ({
      title: "CNC-07 VMC",
      subtitle: "O1207 · HOUSING_OP20",
      status: eventCompleted ? "MACHINING ENDED" : active ? "AUTO SEQUENCE" : "AUTO READY",
      accent: eventCompleted ? "#49dfa0" : active ? "#f4b740" : "#63d9d1",
      rows: eventCompleted
        ? [
            ["BLOCK", "M30 · PROGRAM END", "ok"],
            ["S1 ACTUAL", "0 rpm", "ok"],
            ["CYCLE RESULT", "OK · GOOD 1", "ok"],
            ["DURATION", "42.800 s"],
            ["PROGRAM / TOOL", "O1207 · T08"],
            ["PART", "ING-0718-0127"],
            ["EVENT", "cycle.completed", "ok"],
          ]
        : active
        ? [
            ["BLOCK", "N135 · G81 DRILL"],
            ["S1 COMMAND", "8,500 rpm"],
            ["FEED / Z DEPTH", "420 mm/min · -6.200"],
            ["TOOL", "T08 · Ø10 CARBIDE"],
            ["CLAMP SETPOINT", "5.2 MPa"],
            ["COOLANT SETPOINT", "6.1 bar"],
            ["INTERLOCK CHAIN", "ARMED · MONITORED", "ok"],
          ]
        : [
            ["MODE", "AUTO · EXTERNAL"],
            ["PROGRAM", "O1207 READY"],
            ["S1 ACT / CMD", "0 / 8,500 rpm"],
            ["TOOL LIFE", "74 %"],
            ["PART COUNTER", "126 / 240"],
            ["COOLANT", "LEVEL 82 %", "ok"],
            ["SAFETY", "READY · NO ALARMS", "ok"],
          ],
      footer: eventCompleted
        ? "EVENT #182343 · cycle.completed · CYC-CNC-182342"
        : active
          ? "EVENT #182342 · cycle.started · CYC-CNC-182342"
          : "CYCLE 00:42.8  ·  TARGET 00:45.0  ·  TRACE LOT-0716",
    }),
    [active, eventCompleted],
  );

  useFrame((_, delta) => {
    const { stage, progress } = process.current;
    const doorOpen = -1.52;
    const doorOffset = stage === 1
      ? progress < 0.12
        ? THREE.MathUtils.lerp(doorOpen, 0, windowed(progress, 0, 0.12))
        : progress < 0.74
          ? 0
          : progress < 0.86
            ? THREE.MathUtils.lerp(0, doorOpen, windowed(progress, 0.74, 0.86))
            : doorOpen
      : doorOpen;
    if (door.current) door.current.position.x = doorOffset;
    const machiningFactor = stage === 1
      ? windowed(progress, 0.18, 0.26) * (1 - windowed(progress, 0.66, 0.72))
      : 0;
    if (spindle.current) {
      spindle.current.rotation.y += delta * 18 * machiningFactor;
      spindle.current.position.y = THREE.MathUtils.lerp(1.9, 1.4 + Math.sin(progress * 42) * 0.01, machiningFactor);
    }
  });

  return (
    <group position={[-3.55, 0, -1.85]}>
      <MachinePad active={active} />

      <mesh position={[0, 0.34, -0.08]} castShadow receiveShadow>
        <boxGeometry args={[2.28, 0.48, 2.06]} />
        <meshStandardMaterial color="#2a332e" metalness={0.78} roughness={0.37} />
      </mesh>
      <mesh position={[0, 2.77, -0.08]} castShadow>
        <boxGeometry args={[2.28, 0.22, 2.06]} />
        <meshStandardMaterial color="#333d37" metalness={0.8} roughness={0.34} />
      </mesh>
      {[-0.86, 0.86].map((x) => (
        <mesh key={x} position={[x, 1.7, -0.08]} castShadow>
          <boxGeometry args={[0.3, 1.95, 2.06]} />
          <meshStandardMaterial color="#35403a" metalness={0.82} roughness={0.33} />
        </mesh>
      ))}
      {[-1.03, 1.03].map((x) => (
        <mesh key={`enclosure-${x}`} position={[x, 1.58, -0.08]} castShadow>
          <boxGeometry args={[0.16, 2.18, 2.02]} />
          <meshStandardMaterial color="#303a35" metalness={0.8} roughness={0.34} />
        </mesh>
      ))}
      <mesh position={[0, 1.7, -1.02]} receiveShadow>
        <boxGeometry args={[1.55, 1.92, 0.16]} />
        <meshStandardMaterial color="#121a16" metalness={0.66} roughness={0.48} />
      </mesh>
      <mesh position={[0, 0.64, 0.28]} castShadow>
        <boxGeometry args={[1.48, 0.16, 1.22]} />
        <meshStandardMaterial color="#68746d" metalness={0.9} roughness={0.22} />
      </mesh>

      <group ref={door} position={[-1.52, 1.75, 0.98]}>
        <mesh>
          <boxGeometry args={[1.48, 1.52, 0.045]} />
          <meshPhysicalMaterial
            color="#0b2528"
            emissive="#071b1d"
            emissiveIntensity={0.32}
            metalness={0.32}
            roughness={0.16}
            transparent
            opacity={0.62}
          />
        </mesh>
        {[-0.74, 0.74].map((x) => (
          <mesh key={x} position={[x, 0, 0.03]} castShadow>
            <boxGeometry args={[0.1, 1.62, 0.09]} />
            <meshStandardMaterial color="#818d86" metalness={0.88} roughness={0.23} />
          </mesh>
        ))}
      </group>
      {/* Fixed internal bed: no conveyor or shuttle passes beneath the machine. */}
      <group position={[0, 0, 0.57]}>
        <mesh position={[0, 0.58, 0]} castShadow>
          <boxGeometry args={[1.28, 0.2, 0.86]} />
          <meshStandardMaterial color="#46524c" metalness={0.9} roughness={0.24} />
        </mesh>
        <mesh position={[0, 0.69, 0]} castShadow>
          <boxGeometry args={[0.66, 0.08, 0.52]} />
          <meshStandardMaterial color="#707d76" metalness={0.92} roughness={0.2} />
        </mesh>
        <group position={[0, 0.68, 0]}>
          <mesh castShadow>
            <cylinderGeometry args={[0.24, 0.24, 0.14, 18]} />
            <meshStandardMaterial color="#87938c" metalness={0.94} roughness={0.17} />
          </mesh>
          {[0, Math.PI / 2, Math.PI, -Math.PI / 2].map((angle) => (
            <mesh key={angle} position={[Math.cos(angle) * 0.19, 0.12, Math.sin(angle) * 0.19]} rotation={[0, -angle, 0]}>
              <boxGeometry args={[0.08, 0.1, 0.08]} />
              <meshStandardMaterial color="#b0b8b2" metalness={0.95} roughness={0.16} />
            </mesh>
          ))}
        </group>
      </group>
      <group ref={spindle} position={[0, 1.9, 0.57]}>
        <mesh castShadow>
          <cylinderGeometry args={[0.16, 0.22, 0.72, 18]} />
          <meshStandardMaterial color="#8c9891" metalness={0.94} roughness={0.18} />
        </mesh>
        <mesh position={[0, -0.46, 0]} castShadow>
          <cylinderGeometry args={[0.055, 0.085, 0.22, 14]} />
          <meshStandardMaterial color="#b8c0bb" metalness={0.96} roughness={0.14} />
        </mesh>
      </group>

      <group position={[1.43, 1.78, 0.68]} rotation={[0, -0.2, 0]}>
        <mesh castShadow>
          <boxGeometry args={[0.72, 1.08, 0.16]} />
          <meshStandardMaterial color="#161d19" metalness={0.72} roughness={0.4} />
        </mesh>
        <group position={[0, 0.19, 0.086]}>
          <HmiDisplay width={0.59} height={0.69} spec={cncScreen} />
        </group>
        <mesh position={[-0.24, -0.41, 0.12]} rotation={[Math.PI / 2, 0, 0]}>
          <cylinderGeometry args={[0.07, 0.07, 0.06, 18]} />
          <meshStandardMaterial color="#c83d32" emissive="#741810" emissiveIntensity={0.8} />
        </mesh>
        {[-0.06, 0.1, 0.25].map((x, index) => (
          <mesh key={x} position={[x, -0.41, 0.1]} rotation={[Math.PI / 2, 0, 0]}>
            <cylinderGeometry args={[0.035, 0.035, 0.025, 12]} />
            <meshStandardMaterial
              color={index === 0 ? "#47bd75" : index === 1 ? "#d7a53f" : "#5f6b65"}
              emissive={index === 0 ? "#1e6f42" : index === 1 ? "#6d4a11" : "#111"}
              emissiveIntensity={0.7}
            />
          </mesh>
        ))}
      </group>
      <StatusTower active={active} position={[0.96, 3.02, -0.76]} />
    </group>
  );
}

const ROBOT_BASE = new THREE.Vector3(-1.62, 0.82, -1);
const ROBOT_HOME = new THREE.Vector3(-0.98, 1.72, -1.34);
const ROBOT_RAW_PICK = new THREE.Vector3(-0.55, 0.77, -1.65);
const ROBOT_RAW_APPROACH = new THREE.Vector3(-0.55, 1.28, -1.52);
const ROBOT_CNC_FIXTURE = new THREE.Vector3(-3.55, 0.77, -1.28);
const ROBOT_CNC_APPROACH = new THREE.Vector3(-3.55, 0.77, -0.42);
const ROBOT_CLEARANCE = new THREE.Vector3(-2.2, 1.48, -0.2);
const ROBOT_OUTPUT_PLACE = new THREE.Vector3(0.25, 0.77, 0);
const ROBOT_OUTPUT_APPROACH = new THREE.Vector3(0.25, 1.28, -0.02);
const ROBOT_L1 = 1.3;
const ROBOT_L2 = 1.12;
const ROBOT_TOOL = 0.38;

function robotTarget(stage: number, progress: number, target: THREE.Vector3) {
  if (stage === 0) {
    if (progress < 0.12) return target.copy(ROBOT_HOME);
    if (progress < 0.22) return target.lerpVectors(ROBOT_HOME, ROBOT_RAW_APPROACH, windowed(progress, 0.12, 0.22));
    if (progress < 0.3) return target.lerpVectors(ROBOT_RAW_APPROACH, ROBOT_RAW_PICK, windowed(progress, 0.22, 0.3));
    if (progress < 0.36) return target.copy(ROBOT_RAW_PICK);
    if (progress < 0.46) return target.lerpVectors(ROBOT_RAW_PICK, ROBOT_RAW_APPROACH, windowed(progress, 0.36, 0.46));
    if (progress < 0.58) return target.lerpVectors(ROBOT_RAW_APPROACH, ROBOT_CLEARANCE, windowed(progress, 0.46, 0.58));
    if (progress < 0.68) return target.lerpVectors(ROBOT_CLEARANCE, ROBOT_CNC_APPROACH, windowed(progress, 0.58, 0.68));
    if (progress < 0.8) return target.lerpVectors(ROBOT_CNC_APPROACH, ROBOT_CNC_FIXTURE, windowed(progress, 0.68, 0.8));
    if (progress < 0.84) return target.copy(ROBOT_CNC_FIXTURE);
    if (progress < 0.92) return target.lerpVectors(ROBOT_CNC_FIXTURE, ROBOT_CNC_APPROACH, windowed(progress, 0.84, 0.92));
    return target.lerpVectors(ROBOT_CNC_APPROACH, ROBOT_HOME, windowed(progress, 0.92, 1));
  }
  if (stage !== 2) return target.copy(ROBOT_HOME);
  if (progress < 0.14) return target.lerpVectors(ROBOT_HOME, ROBOT_CNC_APPROACH, windowed(progress, 0, 0.14));
  if (progress < 0.28) return target.lerpVectors(ROBOT_CNC_APPROACH, ROBOT_CNC_FIXTURE, windowed(progress, 0.14, 0.28));
  if (progress < 0.36) return target.copy(ROBOT_CNC_FIXTURE);
  if (progress < 0.5) return target.lerpVectors(ROBOT_CNC_FIXTURE, ROBOT_CNC_APPROACH, windowed(progress, 0.36, 0.5));
  if (progress < 0.62) return target.lerpVectors(ROBOT_CNC_APPROACH, ROBOT_CLEARANCE, windowed(progress, 0.5, 0.62));
  if (progress < 0.74) return target.lerpVectors(ROBOT_CLEARANCE, ROBOT_OUTPUT_APPROACH, windowed(progress, 0.62, 0.74));
  if (progress < 0.82) return target.lerpVectors(ROBOT_OUTPUT_APPROACH, ROBOT_OUTPUT_PLACE, windowed(progress, 0.74, 0.82));
  if (progress < 0.86) return target.copy(ROBOT_OUTPUT_PLACE);
  if (progress < 0.94) return target.lerpVectors(ROBOT_OUTPUT_PLACE, ROBOT_OUTPUT_APPROACH, windowed(progress, 0.86, 0.94));
  return target.lerpVectors(ROBOT_OUTPUT_APPROACH, ROBOT_HOME, windowed(progress, 0.94, 1));
}

function solveRobot(target: THREE.Vector3) {
  const dx = target.x - ROBOT_BASE.x;
  const dz = target.z - ROBOT_BASE.z;
  const yaw = Math.atan2(-dz, dx);
  const radial = Math.hypot(dx, dz);
  const wristRadial = radial;
  const wristHeight = target.y - ROBOT_BASE.y + ROBOT_TOOL;
  const rawD =
    (wristRadial * wristRadial + wristHeight * wristHeight - ROBOT_L1 * ROBOT_L1 - ROBOT_L2 * ROBOT_L2) /
    (2 * ROBOT_L1 * ROBOT_L2);
  if (rawD < -1 || rawD > 1) throw new Error("Factory robot target is outside its physical workspace");
  const elbow = -Math.acos(rawD);
  const shoulder =
    Math.atan2(wristHeight, wristRadial) -
    Math.atan2(ROBOT_L2 * Math.sin(elbow), ROBOT_L1 + ROBOT_L2 * Math.cos(elbow));
  const wrist = -Math.PI / 2 - shoulder - elbow;
  return { elbow, shoulder, wrist, yaw };
}

function RobotCell({ activeStage, stageSettled, process }: { activeStage: number; stageSettled: boolean; process: ProcessRef }) {
  const yawJoint = useRef<THREE.Group>(null);
  const shoulder = useRef<THREE.Group>(null);
  const elbow = useRef<THREE.Group>(null);
  const forearmRoll = useRef<THREE.Group>(null);
  const wrist = useRef<THREE.Group>(null);
  const toolRoll = useRef<THREE.Group>(null);
  const fingerLeft = useRef<THREE.Mesh>(null);
  const fingerRight = useRef<THREE.Mesh>(null);
  const target = useRef(new THREE.Vector3());
  const active = activeStage === 0 || activeStage === 2;
  const loading = activeStage === 0;
  const activeStep = loading ? "RAW NEST → CNC CHUCK" : "CNC CHUCK → VISION";
  const robotScreen = useMemo<HmiSpec>(
    () => ({
      title: "ROBOT-02",
      subtitle: "CNC-07 INTEGRATED MACHINE TENDING",
      status: stageSettled ? "HOME · STATE CURRENT" : active ? "AUTO EXT · RUN" : "AUTO EXT · READY",
      accent: "#49dfa0",
      rows: [
        ["STEP", stageSettled ? "HOME · WAITING" : active ? activeStep : "HOME · WAITING"],
        ["TCP PROGRAM", stageSettled ? "0 mm/s" : active ? "620 mm/s" : "READY"],
        ["SAFE SPEED LIMIT", "1,200 mm/s"],
        ["PAYLOAD", "0.62 kg"],
        ["GRIP AIR", "0.58 MPa", "ok"],
        ["GRIP SENSORS", stageSettled ? "PART RELEASED" : active ? "DUAL CHANNEL ARMED" : "READY", "ok"],
        ["CNC INTERLOCK", active ? "DOOR OPEN · SPINDLE 0" : "READY", "ok"],
      ],
      footer: stageSettled
        ? "STATE RECORD #182345 · POSITION HOME · PLACE CONFIRMED"
        : "MOTORS ON  ·  CNC SAFE POSITION  ·  CELL PROGRAM ACTIVE",
    }),
    [active, activeStep, stageSettled],
  );

  useFrame(() => {
    const { stage, progress } = process.current;
    robotTarget(stage, progress, target.current);
    const joints = solveRobot(target.current);
    if (yawJoint.current) yawJoint.current.rotation.y = joints.yaw;
    if (shoulder.current) shoulder.current.rotation.z = joints.shoulder;
    if (elbow.current) elbow.current.rotation.z = joints.elbow;
    if (forearmRoll.current) forearmRoll.current.rotation.x = 0;
    if (wrist.current) wrist.current.rotation.z = joints.wrist;
    if (toolRoll.current) {
      toolRoll.current.rotation.x = stage === 2 ? windowed(progress, 0.5, 0.72) * (Math.PI / 2) : 0;
    }
    const closing = stage === 0
      ? windowed(progress, 0.3, 0.35) * (1 - windowed(progress, 0.8, 0.84))
      : stage === 2
        ? windowed(progress, 0.28, 0.34) * (1 - windowed(progress, 0.82, 0.86))
        : 0;
    const gap = THREE.MathUtils.lerp(0.19, 0.14, closing);
    if (fingerLeft.current) fingerLeft.current.position.z = gap;
    if (fingerRight.current) fingerRight.current.position.z = -gap;
  });

  return (
    <group>
      {/* Shared skid and rear service gantry visually make robot + CNC one machine-tending cell. */}
      <mesh position={[ROBOT_BASE.x, 0.12, ROBOT_BASE.z]} receiveShadow>
        <boxGeometry args={[2.45, 0.22, 1.85]} />
        <meshStandardMaterial color={active ? "#4c3c20" : "#202823"} metalness={0.72} roughness={0.44} />
      </mesh>
      <mesh position={[ROBOT_BASE.x, 0.42, ROBOT_BASE.z]} castShadow>
        <cylinderGeometry args={[0.58, 0.7, 0.62, 22]} />
        <meshStandardMaterial color="#1a211d" metalness={0.82} roughness={0.34} />
      </mesh>
      <group ref={yawJoint} position={ROBOT_BASE.toArray()}>
        <mesh castShadow>
          <cylinderGeometry args={[0.31, 0.36, 0.3, 18]} />
          <meshStandardMaterial color={gold} metalness={0.68} roughness={0.3} />
        </mesh>
        <group ref={shoulder}>
          <mesh castShadow>
            <sphereGeometry args={[0.3, 18, 14]} />
            <meshStandardMaterial color="#202823" metalness={0.84} roughness={0.24} />
          </mesh>
          <mesh position={[ROBOT_L1 / 2, 0, 0]} castShadow>
            <boxGeometry args={[ROBOT_L1, 0.38, 0.46]} />
            <meshStandardMaterial color={gold} metalness={0.62} roughness={0.3} />
          </mesh>
          <group ref={elbow} position={[ROBOT_L1, 0, 0]}>
            <mesh castShadow>
              <sphereGeometry args={[0.28, 18, 14]} />
              <meshStandardMaterial color="#161d19" metalness={0.82} roughness={0.26} />
            </mesh>
            <mesh position={[ROBOT_L2 / 2, 0, 0]} castShadow>
              <boxGeometry args={[ROBOT_L2, 0.32, 0.4]} />
              <meshStandardMaterial color={gold} metalness={0.65} roughness={0.28} />
            </mesh>
            <group ref={forearmRoll} position={[ROBOT_L2, 0, 0]}>
              <mesh rotation={[0, 0, Math.PI / 2]} castShadow>
                <cylinderGeometry args={[0.21, 0.21, 0.28, 18]} />
                <meshStandardMaterial color="#202823" metalness={0.84} roughness={0.25} />
              </mesh>
              <group ref={wrist}>
                <mesh castShadow>
                  <sphereGeometry args={[0.19, 16, 12]} />
                  <meshStandardMaterial color="#252d29" metalness={0.86} roughness={0.23} />
                </mesh>
                <mesh position={[ROBOT_TOOL / 2, 0, 0]} rotation={[0, 0, -Math.PI / 2]} castShadow>
                  <cylinderGeometry args={[0.1, 0.14, ROBOT_TOOL, 14]} />
                  <meshStandardMaterial color="#a77525" metalness={0.76} roughness={0.29} />
                </mesh>
                <group ref={toolRoll} position={[ROBOT_TOOL, 0, 0]}>
                  <mesh position={[-0.07, 0, 0]} castShadow>
                    <boxGeometry args={[0.14, 0.32, 0.16]} />
                    <meshStandardMaterial color="#252e29" metalness={0.84} roughness={0.26} />
                  </mesh>
                  <mesh ref={fingerLeft} position={[0, 0, 0.19]} castShadow>
                    <boxGeometry args={[0.15, 0.06, 0.06]} />
                    <meshStandardMaterial color="#aeb7b1" metalness={0.93} roughness={0.16} />
                  </mesh>
                  <mesh ref={fingerRight} position={[0, 0, -0.19]} castShadow>
                    <boxGeometry args={[0.15, 0.06, 0.06]} />
                    <meshStandardMaterial color="#aeb7b1" metalness={0.93} roughness={0.16} />
                  </mesh>
                </group>
              </group>
            </group>
          </group>
        </group>
      </group>

      {[ROBOT_BASE.x - 1.4, ROBOT_BASE.x + 1.4].map((x) => (
        <mesh key={x} position={[x, 1.35, -2.72]}>
          <boxGeometry args={[0.07, 2.25, 0.07]} />
          <meshStandardMaterial color="#69746e" metalness={0.86} roughness={0.24} />
        </mesh>
      ))}
      <mesh position={[ROBOT_BASE.x, 2.45, -2.72]}>
        <boxGeometry args={[2.87, 0.07, 0.07]} />
        <meshStandardMaterial color="#69746e" metalness={0.86} roughness={0.24} />
      </mesh>
      <mesh position={[-2.55, 2.52, -2.72]} castShadow>
        <boxGeometry args={[2.05, 0.16, 0.16]} />
        <meshStandardMaterial color="#b7832e" metalness={0.75} roughness={0.3} />
      </mesh>
      {[ROBOT_OUTPUT_PLACE].map((nest, index) => (
        <group key={index} position={[nest.x, nest.y - 0.08, nest.z]}>
          <mesh castShadow>
            <boxGeometry args={[0.52, 0.1, 0.48]} />
            <meshStandardMaterial color="#4d5a53" metalness={0.88} roughness={0.25} />
          </mesh>
          {[[-0.2, -0.18], [-0.2, 0.18], [0.2, -0.18], [0.2, 0.18]].map(([x, z]) => (
            <mesh key={`${x}-${z}`} position={[x, 0.08, z]}>
              <cylinderGeometry args={[0.025, 0.025, 0.08, 10]} />
              <meshStandardMaterial color="#aeb7b1" metalness={0.92} roughness={0.18} />
            </mesh>
          ))}
        </group>
      ))}
      <group position={[-0.18, 1.62, -2.66]}>
        <mesh position={[0, 0, -0.05]} castShadow>
          <boxGeometry args={[0.72, 0.74, 0.12]} />
          <meshStandardMaterial color="#151d19" metalness={0.76} roughness={0.35} />
        </mesh>
        <HmiDisplay width={0.62} height={0.6} spec={robotScreen} />
      </group>
      <StatusTower active={active} position={[-0.52, 1.55, -2.55]} />
    </group>
  );
}

function VisionStation({ active, stageSettled, process }: { active: boolean; stageSettled: boolean; process: ProcessRef }) {
  const scan = useRef<THREE.Mesh>(null);
  const stopPin = useRef<THREE.Group>(null);
  const visionScreen = useMemo<HmiSpec>(
    () => ({
      title: "VISION-03 3D",
      subtitle: "HOUSING_TOP_V3.2 · LOT-0716",
      status: stageSettled ? "RESULT · PASS" : active ? "AUTO · ACQUIRE" : "ONLINE · READY",
      accent: stageSettled ? "#49dfa0" : active ? "#63d9d1" : "#49dfa0",
      rows: active && !stageSettled
        ? [
            ["POCKET WIDTH", "MEASURING…"],
            ["HOLE PITCH", "PENDING"],
            ["CHAMFER", "PENDING"],
            ["SURFACE", "PENDING"],
            ["RESULT / SCORE", "NOT AVAILABLE", "warn"],
            ["EXPOSURE / GAIN", "320 µs / +1.5 dB"],
            ["CAMERAS", "3 / 3 ACQUIRING", "ok"],
          ]
        : [
            ["POCKET WIDTH", "42.03 mm · PASS", "ok"],
            ["HOLE PITCH", "32.02 mm · PASS", "ok"],
            ["CHAMFER", "4 / 4 · PASS", "ok"],
            ["MAX SCRATCH", "0.18 / 0.30 mm", "ok"],
            ["RESULT / SCORE", "PASS · 97.8", "ok"],
            ["EXPOSURE / GAIN", "320 µs / +1.5 dB"],
            ["CAMERAS", "3 / 3 ONLINE", "ok"],
          ],
      footer: stageSettled
        ? "RESULT RECORDED #182347 · IMG-182347 · SOURCE VISION-03"
        : active
          ? "TRIGGER → BUSY  ·  RESULT PENDING  ·  RELEASE BLOCKED"
          : "LAST RESULT PASS  ·  CALIBRATION VALID  ·  NO FAIL",
    }),
    [active, stageSettled],
  );

  useFrame(() => {
    const progress = active ? process.current.progress : 0;
    if (scan.current) scan.current.visible = active && progress >= 0.34 && progress <= 0.56;
    if (stopPin.current) {
      const stopRaised = active && progress >= 0.2 && progress <= 0.72;
      stopPin.current.position.y = stopRaised ? 0.76 : 0.48;
    }
  });

  return (
    <group position={[3.55, 0, 0]}>
      <MachinePad active={active} />
      {[-0.82, 0.82].map((z) => (
        <group key={z} position={[0, 1.5, z]}>
          <mesh castShadow>
            <boxGeometry args={[0.24, 2.5, 0.2]} />
            <meshStandardMaterial color="#4e5e57" metalness={0.86} roughness={0.28} />
          </mesh>
          <mesh position={[0.08, -0.55, 0]}>
            <boxGeometry args={[0.08, 0.82, 0.12]} />
            <meshStandardMaterial color={cyan} emissive={cyan} emissiveIntensity={active ? 1.6 : 0.32} />
          </mesh>
        </group>
      ))}
      <mesh position={[0, 2.72, 0]} castShadow>
        <boxGeometry args={[0.42, 0.32, 1.84]} />
        <meshStandardMaterial color="#35423c" metalness={0.82} roughness={0.3} />
      </mesh>
      {[-0.38, 0, 0.38].map((z) => (
        <group key={z} position={[0, 2.52, z]}>
          <mesh castShadow>
            <boxGeometry args={[0.28, 0.22, 0.25]} />
            <meshStandardMaterial color="#111b18" metalness={0.72} roughness={0.3} />
          </mesh>
          <mesh position={[0, -0.15, 0]}>
            <cylinderGeometry args={[0.065, 0.065, 0.03, 18]} />
            <meshStandardMaterial color="#78e9df" emissive={cyan} emissiveIntensity={active ? 2.6 : 0.5} />
          </mesh>
        </group>
      ))}
      {[-0.68, 0.68].map((z) => (
        <group key={z} position={[0, 1.18, z]} rotation={[Math.PI / 2, 0, 0]}>
          <mesh castShadow>
            <boxGeometry args={[0.3, 0.18, 0.24]} />
            <meshStandardMaterial color="#17211d" metalness={0.76} roughness={0.3} />
          </mesh>
          <mesh position={[0, -0.12, 0]}>
            <cylinderGeometry args={[0.065, 0.065, 0.06, 18]} />
            <meshStandardMaterial color="#68dcd2" emissive={cyan} emissiveIntensity={active ? 2.2 : 0.5} />
          </mesh>
        </group>
      ))}
      <mesh ref={scan} position={[0, 1.26, 0]}>
        <boxGeometry args={[0.72, 0.025, 1.38]} />
        <meshBasicMaterial color={cyan} transparent opacity={0.34} depthWrite={false} />
      </mesh>
      <group ref={stopPin} position={[0.205, 0.48, 0]}>
        <mesh>
          <cylinderGeometry args={[0.045, 0.045, 0.4, 12]} />
          <meshStandardMaterial color="#d3a743" metalness={0.72} roughness={0.28} />
        </mesh>
      </group>
      <group position={[0.82, 1.7, 1.22]} rotation={[0, -0.08, 0]}>
        <mesh castShadow>
          <boxGeometry args={[0.92, 0.86, 0.16]} />
          <meshStandardMaterial color="#19211d" metalness={0.75} roughness={0.38} />
        </mesh>
        <group position={[0, 0.02, 0.086]}>
          <HmiDisplay width={0.8} height={0.68} spec={visionScreen} />
        </group>
      </group>
      <StatusTower active={active} position={[0.72, 2.9, -1.06]} />
    </group>
  );
}

function InspectionExitBuffer() {
  return (
    <group position={[5.15, 0, 0]}>
      <group position={[0, 0.88, -0.78]} rotation={[Math.PI / 2, 0, 0]}>
        <mesh castShadow>
          <cylinderGeometry args={[0.1, 0.1, 0.58, 16]} />
          <meshStandardMaterial color="#4d5a54" metalness={0.84} roughness={0.28} />
        </mesh>
        <mesh position={[0, -0.38, 0]}>
          <cylinderGeometry args={[0.035, 0.035, 0.3, 12]} />
          <meshStandardMaterial color="#b7c0bb" metalness={0.94} roughness={0.16} />
        </mesh>
      </group>
      <mesh position={[0, 0.88, -0.42]} castShadow>
        <boxGeometry args={[0.52, 0.34, 0.06]} />
        <meshStandardMaterial color="#66736c" metalness={0.86} roughness={0.27} />
      </mesh>
      <mesh position={[0, 0.58, 0.82]} rotation={[-0.23, 0, 0]} castShadow>
        <boxGeometry args={[0.7, 0.08, 1.18]} />
        <meshStandardMaterial color="#515e57" metalness={0.82} roughness={0.32} />
      </mesh>
      <group position={[0, 0.37, 1.12]}>
        <mesh castShadow>
          <boxGeometry args={[0.74, 0.7, 0.52]} />
          <meshStandardMaterial color="#372d2a" metalness={0.48} roughness={0.54} />
        </mesh>
        <mesh position={[0, 0.37, 0]}>
          <boxGeometry args={[0.76, 0.06, 0.54]} />
          <meshStandardMaterial color="#722f2a" metalness={0.58} roughness={0.42} />
        </mesh>
        <mesh position={[0.25, 0.39, 0.27]}>
          <boxGeometry args={[0.09, 0.12, 0.04]} />
          <meshStandardMaterial color="#c5a856" metalness={0.72} roughness={0.3} />
        </mesh>
      </group>
      <group position={[-0.31, 0.85, 0.5]}>
        <mesh>
          <boxGeometry args={[0.05, 0.42, 0.05]} />
          <meshStandardMaterial color="#707d76" metalness={0.86} roughness={0.25} />
        </mesh>
        <mesh position={[0.62, 0, 0]}>
          <boxGeometry args={[0.05, 0.42, 0.05]} />
          <meshStandardMaterial color="#707d76" metalness={0.86} roughness={0.25} />
        </mesh>
        <mesh position={[0.62, 0.16, 0]}>
          <boxGeometry args={[0.03, 0.08, 0.03]} />
          <meshStandardMaterial color="#56dfa2" emissive="#35b67a" emissiveIntensity={1.8} />
        </mesh>
      </group>
    </group>
  );
}

function ConveyorSegment({ end, process, side, start }: { end: number; process: ProcessRef; side: "left" | "right"; start: number }) {
  const rollersRef = useRef<THREE.Group>(null);
  const length = end - start;
  const rollers = useMemo(
    () => Array.from({ length: Math.floor(length / 0.52) }, (_, index) => start + 0.26 + index * 0.52),
    [length, start],
  );
  const railRanges = (z: number): [number, number][] => {
    if (side === "right") return [[start, end]];
    if (z < 0) return [[start, -7.42], [-6.58, -4], [-3.1, end]];
    return [[start, end]];
  };

  useFrame((_, delta) => {
    if (!rollersRef.current) return;
    const { stage, progress } = process.current;
    const moving = side === "left"
      ? stage === 0 || (stage === 2 && progress < 0.22)
      : stage === 3 && (progress < 0.28 || progress > 0.72);
    if (!moving) return;
    rollersRef.current.children.forEach((roller) => {
      roller.children[0]?.rotateY(-delta * 8.7);
    });
  });

  return (
    <group>
      {[-0.61, 0.61].flatMap((z) =>
        railRanges(z).map(([railStart, railEnd]) => (
          <mesh key={`${z}-${railStart}-${railEnd}`} position={[(railStart + railEnd) / 2, 0.57, z]} castShadow>
            <boxGeometry args={[railEnd - railStart, 0.28, 0.14]} />
            <meshStandardMaterial color="#313b36" metalness={0.88} roughness={0.28} />
          </mesh>
        )),
      )}
      <group ref={rollersRef}>
        {rollers.map((x) => (
          <group key={x} position={[x, 0.62, 0]} rotation={[Math.PI / 2, 0, 0]}>
            <mesh castShadow>
              <cylinderGeometry args={[0.09, 0.09, 1.18, 12]} />
              <meshStandardMaterial color="#66716b" metalness={0.92} roughness={0.22} />
            </mesh>
          </group>
        ))}
      </group>
      {(side === "left" ? [-8.75, -5.4, -1.72] : [1.72, 5.15]).map((x) => (
        <group key={x} position={[x, 0.28, 0]}>
          {[-0.52, 0.52].map((z) => (
            <mesh key={z} position={[0, 0, z]}>
              <boxGeometry args={[0.13, 0.56, 0.13]} />
              <meshStandardMaterial color="#333d38" metalness={0.84} roughness={0.3} />
            </mesh>
          ))}
        </group>
      ))}
    </group>
  );
}

function Conveyor({ process }: { process: ProcessRef }) {
  return (
    <group>
      {/* Only the downstream line begins at the robot's output nest. */}
      <ConveyorSegment start={0.15} end={5.35} side="right" process={process} />
      {[-0.58, 0.58].map((x) => (
        <group key={x} position={[x, 0.82, 0]}>
          {[-0.42, 0.42].map((z) => (
            <mesh key={z} position={[0, 0, z]} castShadow>
              <boxGeometry args={[0.08, 0.24, 0.08]} />
              <meshStandardMaterial color="#a28445" metalness={0.76} roughness={0.32} />
            </mesh>
          ))}
        </group>
      ))}
    </group>
  );
}

function WorkpieceFlow({ process }: { process: ProcessRef }) {
  const part = useRef<THREE.Group>(null);
  const target = useRef(new THREE.Vector3());

  useFrame(() => {
    if (!part.current) return;
    const { stage, progress } = process.current;
    part.current.visible = true;
    part.current.rotation.set(0, 0, 0);
    const nextPosition = target.current;
    nextPosition.set(0, 0.77, 0);

    if (stage === 0) {
      if (progress < 0.36) {
        nextPosition.copy(ROBOT_RAW_PICK);
      } else if (progress < 0.84) {
        robotTarget(0, progress, nextPosition);
      } else {
        nextPosition.copy(ROBOT_CNC_FIXTURE);
      }
    } else if (stage === 1) {
      nextPosition.copy(ROBOT_CNC_FIXTURE);
    } else if (stage === 2) {
      if (progress < 0.36) {
        nextPosition.copy(ROBOT_CNC_FIXTURE);
      } else if (progress < 0.82) {
        robotTarget(2, progress, nextPosition);
      } else {
        nextPosition.copy(ROBOT_OUTPUT_PLACE);
      }
      part.current.rotation.y = windowed(progress, 0.5, 0.72) * (Math.PI / 2);
    } else if (stage === 3) {
      part.current.rotation.y = Math.PI / 2;
      nextPosition.x = progress < 0.28
        ? THREE.MathUtils.lerp(ROBOT_OUTPUT_PLACE.x, 3.55, windowed(progress, 0, 0.28))
        : progress < 0.72
          ? 3.55
          : THREE.MathUtils.lerp(3.55, 5, windowed(progress, 0.72, 1));
    } else if (stage === 4) {
      // The inspected part stays in the guarded exit buffer while its result
      // is submitted from outside the fence. Disposition stays external.
      part.current.rotation.y = Math.PI / 2;
      nextPosition.x = 5;
    } else {
      part.current.visible = false;
    }
    part.current.position.copy(nextPosition);
  });

  return (
    <group ref={part} position={ROBOT_RAW_PICK.toArray()}>
      <mesh castShadow>
        <boxGeometry args={[0.32, 0.12, 0.26]} />
        <meshStandardMaterial color="#aeb8b0" metalness={0.86} roughness={0.2} />
      </mesh>
      <mesh position={[0, 0.068, 0]}>
        <boxGeometry args={[0.18, 0.025, 0.14]} />
        <meshStandardMaterial color={gold} emissive={gold} emissiveIntensity={0.72} />
      </mesh>
    </group>
  );
}

function IndustrialEdgeCabinet({ activeStage, stageSettled }: { activeStage: number; stageSettled: boolean }) {
  const currentStage = FACTORY_STAGES[activeStage] ?? FACTORY_STAGES[0];
  const stageData = FACTORY_STAGE_DATA[activeStage] ?? FACTORY_STAGE_DATA[0];
  const awaitingInspection = currentStage.view === "manual" && !stageSettled;
  const currentDatum = stageData.kind === "machining"
    ? stageSettled ? stageData.completed : stageData.started
    : stageSettled ? stageData.settled : stageData.active;
  const dataType = "eventType" in currentDatum ? currentDatum.eventType : currentDatum.recordType;
  const displayDataType = stageData.kind === "machining"
    ? dataType
    : currentDatum.recordType.replaceAll("_", " ").toUpperCase();
  const edgeScreen = useMemo<HmiSpec>(
    () => ({
      title: "INGOT EDGE 01",
      subtitle: `INGEST · ${currentStage.code}`,
      status: awaitingInspection
        ? "MANUAL INSPECTION"
        : stageData.kind === "machining"
          ? stageSettled ? "MACHINING ENDED" : "MACHINING EVENT"
          : stageSettled ? "DATA RECORDED" : "DATA LIVE",
      accent: awaitingInspection ? "#f4b740" : "#49dfa0",
      rows: [
        ["CURRENT SOURCE", currentStage.code, "ok"],
        [stageData.kind === "machining" ? "EVENT TYPE" : "RECORD TYPE", displayDataType],
        ["SOURCE LINK", currentStage.source],
        ["CAPTURE MODE", currentStage.view === "manual" ? stageSettled ? "INSTRUMENT · SAVED" : "INSTRUMENT + HUMAN" : "AUTOMATIC", currentStage.view === "manual" ? "warn" : "ok"],
        ["COLLECT P99", "18 ms"],
        ["LOCAL STORE", awaitingInspection ? "SAVE AFTER INSPECTION" : "SAVED · PART LINKED", awaitingInspection ? "warn" : "ok"],
        ["UPLINK", "TLS · CONNECTED", "ok"],
      ],
      footer: awaitingInspection
        ? "INSTRUMENT VALUE + INSPECTOR CONFIRMATION · OT VLAN 120"
        : "STORE → PART HISTORY → SYNC  ·  OT VLAN 120  ·  UPS 98 %",
    }),
    [awaitingInspection, currentStage.code, currentStage.source, currentStage.view, displayDataType, stageData.kind, stageSettled],
  );

  return (
    <group position={[10.3, 0, -5.42]}>
      <mesh position={[0, 1.18, -0.08]} castShadow>
        <boxGeometry args={[1.2, 2.26, 0.34]} />
        <meshStandardMaterial color="#303a35" metalness={0.82} roughness={0.36} />
      </mesh>
      {[-0.56, 0.56].flatMap((x) =>
        [0.1, 2.26].map((y) => (
          <mesh key={`${x}-${y}`} position={[x, y, 0.12]}>
            <boxGeometry args={[0.06, 0.08, 0.5]} />
            <meshStandardMaterial color="#707a74" metalness={0.88} roughness={0.24} />
          </mesh>
        )),
      )}
      <mesh position={[0, 1.18, 0.24]}>
        <boxGeometry args={[1.12, 2.16, 0.04]} />
        <meshPhysicalMaterial
          color="#7d9f99"
          transparent
          opacity={0.14}
          depthWrite={false}
          roughness={0.12}
          metalness={0.08}
        />
      </mesh>

      <group position={[0, 1.33, 0.14]}>
        <mesh castShadow>
          <boxGeometry args={[0.72, 0.42, 0.14]} />
          <meshStandardMaterial color="#1b2220" metalness={0.86} roughness={0.26} />
        </mesh>
        {Array.from({ length: 9 }, (_, index) => (
          <mesh key={index} position={[-0.3 + index * 0.075, 0, 0.085]}>
            <boxGeometry args={[0.025, 0.34, 0.035]} />
            <meshStandardMaterial color="#5f6964" metalness={0.92} roughness={0.18} />
          </mesh>
        ))}
        {[-0.27, -0.17, -0.07, 0.07, 0.17, 0.27].map((x, index) => (
          <mesh key={x} position={[x, -0.15, 0.11]}>
            <boxGeometry args={[0.045, 0.035, 0.02]} />
            <meshStandardMaterial
              color={index < 2 ? "#58dfa2" : index === 2 ? "#e0aa42" : "#62cfd0"}
              emissive={index < 2 ? "#2ab875" : index === 2 ? "#76500f" : "#237a79"}
              emissiveIntensity={1.45}
            />
          </mesh>
        ))}
      </group>

      <group position={[0, 1.84, 0.14]}>
        <mesh castShadow>
          <boxGeometry args={[0.76, 0.2, 0.14]} />
          <meshStandardMaterial color="#202925" metalness={0.82} roughness={0.28} />
        </mesh>
        {Array.from({ length: 8 }, (_, index) => (
          <group key={index} position={[-0.3 + index * 0.085, -0.01, 0.08]}>
            <mesh>
              <boxGeometry args={[0.055, 0.075, 0.02]} />
              <meshStandardMaterial color="#111815" metalness={0.72} roughness={0.35} />
            </mesh>
            <mesh position={[0, 0.055, 0.012]}>
              <boxGeometry args={[0.02, 0.02, 0.01]} />
              <meshStandardMaterial color="#56dca0" emissive="#2db878" emissiveIntensity={1.3} />
            </mesh>
          </group>
        ))}
      </group>

      <group position={[0, 0.68, 0.14]}>
        <mesh castShadow>
          <boxGeometry args={[0.64, 0.34, 0.14]} />
          <meshStandardMaterial color="#272f2c" metalness={0.78} roughness={0.34} />
        </mesh>
        <mesh position={[-0.21, 0, 0.08]}>
          <boxGeometry args={[0.12, 0.13, 0.02]} />
          <meshStandardMaterial color="#19211e" />
        </mesh>
        <mesh position={[0.21, 0, 0.08]}>
          <boxGeometry args={[0.12, 0.13, 0.02]} />
          <meshStandardMaterial color="#58dda0" emissive="#2cb779" emissiveIntensity={0.8} />
        </mesh>
      </group>

      <group position={[0, 1.18, 0.27]}>
        <mesh position={[0, 0.94, -0.02]}>
          <boxGeometry args={[0.58, 0.42, 0.07]} />
          <meshStandardMaterial color="#111916" metalness={0.74} roughness={0.32} />
        </mesh>
        <group position={[0, 0.94, 0.02]}>
          <HmiDisplay width={0.5} height={0.34} spec={edgeScreen} />
        </group>
      </group>
    </group>
  );
}

function DataPacket({
  active,
  curve,
  delayed,
  offset,
  process,
  reducedMotion,
}: {
  active: boolean;
  curve: THREE.Curve<THREE.Vector3>;
  delayed: boolean;
  offset: number;
  process: ProcessRef;
  reducedMotion: boolean;
}) {
  const packet = useRef<THREE.Mesh>(null);

  useFrame((state) => {
    if (!packet.current) return;
    const released = !delayed || process.current.progress >= 0.58;
    packet.current.visible = active && released;
    if (!active || !released) return;
    const progress = reducedMotion ? offset : (state.clock.elapsedTime * 0.19 + offset) % 1;
    packet.current.position.copy(curve.getPointAt(progress));
  });

  return (
    <mesh ref={packet} visible={active}>
      <sphereGeometry args={[0.055, 10, 8]} />
      <meshBasicMaterial color={offset > 0.5 ? goldBright : cyan} />
    </mesh>
  );
}

function makeCableRoute(points: THREE.Vector3[]) {
  const route = new THREE.CurvePath<THREE.Vector3>();
  for (let index = 0; index < points.length - 1; index += 1) {
    route.add(new THREE.LineCurve3(points[index], points[index + 1]));
  }
  return route;
}

function DataFabric({ activeStage, process, reducedMotion }: { activeStage: number; process: ProcessRef; reducedMotion: boolean }) {
  const curves = useMemo(
    () => {
      const sources = [
        new THREE.Vector3(-7.25, 2.8, -2.35),
        new THREE.Vector3(-3.55, 3.02, -2.61),
        new THREE.Vector3(1.48, 2.02, -2.66),
        new THREE.Vector3(3.55, 2.72, -0.82),
            new THREE.Vector3(-3, 1.46, 1.94),
      ];
      return sources.map((source, index) => {
        const laneZ = -4.34 - index * 0.045;
        const laneY = 4.05 + index * 0.035;
        return makeCableRoute([
          source,
          new THREE.Vector3(source.x, laneY, source.z),
          new THREE.Vector3(source.x, laneY, laneZ),
          new THREE.Vector3(10.3, laneY, laneZ),
          new THREE.Vector3(10.3, 2.36, -5.08),
        ]);
      });
    },
    [],
  );

  return (
    <group>
      <group>
        {[-4.68, -4.22].map((z) => (
          <mesh key={z} position={[1.05, 4.16, z]}>
            <boxGeometry args={[19.1, 0.055, 0.055]} />
            <meshStandardMaterial color="#66716b" metalness={0.88} roughness={0.26} />
          </mesh>
        ))}
        {Array.from({ length: 28 }, (_, index) => -8.35 + index * 0.7).map((x) => (
          <mesh key={x} position={[x, 4.14, -4.45]}>
            <boxGeometry args={[0.04, 0.04, 0.48]} />
            <meshStandardMaterial color="#5b6761" metalness={0.86} roughness={0.28} />
          </mesh>
        ))}
      </group>
      {curves.map((curve, index) => (
        <group key={index}>
          <mesh>
            <tubeGeometry args={[curve, 80, 0.018, 7, false]} />
            <meshStandardMaterial
              color={index === activeStage ? (index % 2 ? "#d5a64c" : "#61d9d1") : "#2f3935"}
              emissive={index === activeStage ? (index % 2 ? "#916019" : "#267c77") : "#111714"}
              emissiveIntensity={index === activeStage ? 1.25 : 0.12}
              metalness={0.22}
              roughness={0.52}
            />
          </mesh>
          {[0, 0.42, 0.78].map((offset) => (
            <DataPacket
              active={index === activeStage}
              key={offset}
              curve={curve}
              delayed={index === 4}
              offset={(offset + index * 0.11) % 1}
              process={process}
              reducedMotion={reducedMotion}
            />
          ))}
        </group>
      ))}
    </group>
  );
}

function FactoryScene({ activeStage, stageSettled, stageRevision, paused, reducedMotion, view }: { activeStage: number; stageSettled: boolean; stageRevision: number; paused: boolean; reducedMotion: boolean; view: FactoryView }) {
  const process = useProcessClock(activeStage, stageRevision, paused, reducedMotion);

  return (
    <>
      <color attach="background" args={["#c9d0cc"]} />
      <fog attach="fog" args={["#c9d0cc", 22, 48]} />
      <ambientLight intensity={1.08} color="#f3fff8" />
      <hemisphereLight args={["#f7fffb", "#7b8580", 1.35]} />
      <directionalLight
        position={[4, 10, 8]}
        color="#fffef5"
        intensity={3.2}
        castShadow
        shadow-mapSize-width={1024}
        shadow-mapSize-height={1024}
        shadow-camera-near={1}
        shadow-camera-far={28}
        shadow-camera-left={-13}
        shadow-camera-right={13}
        shadow-camera-top={10}
        shadow-camera-bottom={-7}
      />
      <spotLight position={[-8, 6, 5]} color="#c7fffb" intensity={14} angle={0.42} penumbra={0.85} distance={18} />
      <spotLight position={[8, 5, 4]} color="#fff0c7" intensity={12} angle={0.46} penumbra={0.88} distance={16} />

      <FactoryShell />
      <SafetyPerimeter />
      <Conveyor process={process} />
      <RawBlankMagazine active={activeStage === 0} />
      <CncMachine activeStage={activeStage} eventCompleted={activeStage === 1 && stageSettled} process={process} />
      <RobotCell activeStage={activeStage} stageSettled={(activeStage === 0 || activeStage === 2) && stageSettled} process={process} />
      <VisionStation active={activeStage === 3} stageSettled={activeStage === 3 && stageSettled} process={process} />
      <InspectionExitBuffer />
      <WorkpieceFlow process={process} />
      <CellOverviewConsole activeStage={activeStage} stageSettled={stageSettled} process={process} />
      <CellTechnician active={activeStage === 4} process={process} />
      <IndustrialEdgeCabinet activeStage={activeStage} stageSettled={stageSettled} />
      <DataFabric activeStage={activeStage} process={process} reducedMotion={reducedMotion} />
      <CameraRig view={view} reducedMotion={reducedMotion} />
    </>
  );
}

export default function FactoryScene3D({ activeStage, stageSettled, stageRevision, fallback, paused, view }: FactoryScene3DProps) {
  const reducedMotion =
    typeof window !== "undefined" &&
    window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  return (
    <div className="factory-scene-3d" aria-hidden="true">
      <Canvas
        camera={{ position: CAMERA_VIEWS.feeding.position, fov: 42, near: 0.1, far: 80 }}
        dpr={[1, 1.6]}
        fallback={fallback}
        frameloop={reducedMotion || paused ? "demand" : "always"}
        gl={{ antialias: true, alpha: false, powerPreference: "high-performance" }}
        shadows="percentage"
        onCreated={({ gl }) => {
          gl.toneMapping = THREE.ACESFilmicToneMapping;
          gl.toneMappingExposure = 1.28;
          gl.outputColorSpace = THREE.SRGBColorSpace;
          gl.shadowMap.type = THREE.PCFShadowMap;
        }}
      >
        <FactoryScene
          activeStage={activeStage}
          stageSettled={stageSettled}
          stageRevision={stageRevision}
          paused={paused}
          reducedMotion={reducedMotion}
          view={view}
        />
      </Canvas>
    </div>
  );
}
