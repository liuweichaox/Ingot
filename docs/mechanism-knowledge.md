# 机理知识设计

> 文档状态：**分阶段实现中**。声明内核、关系型存储、工作台、证据升级生命周期、约束排序、知识快照门禁和建议使用追溯已经实现；Bayesian prior 与机理残差融合仍按本文后续阶段推进。

## 1. 目标

机理知识能力用于把工程师经验、工艺资料和实验结论转化为带来源、带适用范围、可审核、可证伪、可版本化的工程资产，并在证据允许时增强候选原因、实验设计和下一步实验建议。

它不建立第二条业务主线。所有知识仍沿用 Ingot 的正式证据闭环：

```text
原始来源 → 可定位片段 → 机理声明草稿 → 工程师审核
        → 假设与实验 → 支持或反对证据 → 正式知识版本
        → 建议约束、先验或解释 → 实际结果 → 继续修订
```

没有机理知识时，采集、运行重建、质量关联、比较、统计分析、DOE 和批准范围内的数据驱动建议继续工作。机理知识是可选增强，不是系统启动条件。

## 2. 非目标

本模块不负责：

- 把文档检索结果直接当作已验证工艺结论；
- 让语言模型直接生成数值工艺设定；
- 用机理模型取代 PLC、DCS、设备联锁或现场安全规则；
- 自动批准知识、实验或参数建议；
- 在模型参数中隐藏正式知识，或把模型记忆作为业务记录源；
- 因为存在物理解释就绕过数据准入、实验验证或影子验证；
- 把单一设备、材料或产品上的经验静默推广到其他场景。

## 3. 现有基础与缺口

当前代码已经具备：

- `KnowledgeSource`：原始文件、哈希、项目范围和复核状态；
- `KnowledgeRecord`：带页码、工作表、单元格或区域引用的知识片段；
- PDF、Excel、CSV、文本和图片等来源的提取扩展点；
- `ResearchHypothesis`：变量、预期效应、混杂、适用范围和正反证据；
- `MechanismModelVersion`：可执行、版本化、可审计的仿射机理模型；
- `MechanismFusionDefinition`：标定、后处理、机理特征和集成四种融合方式；
- 假设、实验、结果、工艺操作域、知识声明和独立复核主线；
- Agent 只检索经过人工复核的项目知识。
- 关系型 `MechanismClaim` 版本、变量、适用范围、约束、证据、审核和冲突记录；
- 机理知识工作台：来源异步提取、声明录入、独立审核、冲突登记与独立解决；
- 知识来源、上下文、片段、结构化值和引用位置的关系型存储，正式读写不再使用 JSONB payload；
- 项目内证据引用与内容哈希校验，以及建议知识使用记录表。
- 作用链、时间特征、交互、失效条件和反证条件的关系型研发假设；
- `reviewed → supported → validated → active → retired` 主生命周期，并允许任一晋级状态经正式反证实验进入 `falsified`；支持、验证和反证均由预注册效应与置信区间自动判定并记录人工评价；
- 仅对项目上下文匹配、无开放冲突的 `active` 声明应用硬边界和软范围候选排序；
- 优化、历史回放、影子证据和受控在线准入冻结同一个机理知识快照；知识变化后旧实验不能批准或启动；
- 研发实验页面展示实际采用的声明、版本、用途和内容哈希。

关键缺口是：

1. 工作台尚未提供大模型辅助的语义提取草稿；
2. 可执行机理模型的状态转换仍需统一纳入同一证据升级规则；
3. Bayesian prior、机理特征和残差模型尚未接入推荐；
4. 已有机理快照专项回放、影子和在线门禁；仍需增加“有知识 vs 纯数据”的成对效果报告与长期校准指标。

## 4. 三种建议模式

Platform 在每次建议前计算能力档案，Optimizer 不自行猜测当前模式。

### 4.1 数据模式 `data-only`

准入条件：

- 运行与质量数据通过分析准入；
- 参数硬边界、设备限制和工程安全范围明确；
- 有足够的历史观察、安全基线或批准的初始 DOE。

允许同类比较、DOE、候选排序、已批准范围内插值和影子建议。默认禁止跨场景迁移和超出已批准范围的外推。解释只能陈述数据依据，不声称物理原因。

### 4.2 知识增强模式 `knowledge-assisted`

在数据模式基础上，存在与当前产品、材料、设备、工装和阶段匹配、无开放冲突且已激活的机理声明。声明可以用于：

- 缩小候选空间；
- 增加硬约束或软约束；
- 提供方向、阈值、交互或时间响应预期；
- 排序候选原因；
- 选择更有证伪价值的实验；
- 生成带引用的建议解释。

定性知识不能自动变成精确数值先验。

### 4.3 机理融合模式 `mechanism-fused`

在知识增强模式基础上，存在经过验证、启用且上下文匹配的可执行机理模型和融合定义。可用于机理特征、标定、残差学习、后处理或受控集成。

机理模型不匹配当前上下文、输入超范围、版本失效或验证不足时，系统必须降级而不是强制执行。

## 5. 核心概念

### 5.1 知识来源 `KnowledgeSource`

不可变原始文件及其元数据。包括文档、表格、图片、现场记录、实验复盘和工程师说明。原文件保存在受控对象存储或文件存储中，Platform 保存哈希、媒体类型、项目范围和审计记录。

### 5.2 知识片段 `KnowledgeFragment`

从来源中确定性提取的可定位内容。片段保留页码、工作表、单元格、图像区域、提取器版本、置信度和内容哈希。片段是证据，不是结论。

### 5.3 机理声明 `MechanismClaim`

对变量关系、阈值、交互、时间响应、约束或失效模式的结构化工程陈述。声明必须能说明适用范围和反证条件，并引用至少一个来源片段或系统证据。

声明类型首期限定为：

| 类型 | 含义 | 可用于 |
|---|---|---|
| `qualitative` | 定性作用链 | 候选原因、解释、实验草案 |
| `monotonic` | 单调增加或减少 | 候选排序、软先验 |
| `threshold` | 阈值前后行为不同 | 候选空间、实验边界 |
| `interaction` | 两个或多个变量共同作用 | 交互实验设计 |
| `temporal` | 延迟、阶段或曲线形态 | 轨迹诊断、阶段特征 |
| `constraint` | 已审核工程约束 | 硬边界或软约束 |
| `failure-mode` | 失效模式及可观测征兆 | 停止、拒绝和追因 |
| `executable-model` | 引用可执行机理模型 | 机理融合 |

### 5.4 可执行机理模型 `MechanismModelVersion`

由一个或多个已审核声明支撑的确定性模型。现有仿射模型继续作为首个受控实现；未来方程、查表、状态空间或外部仿真只能通过白名单模型类型扩展，不能上传任意可执行代码。

### 5.5 知识使用记录 `RecommendationKnowledgeUsage`

冻结某次建议使用的声明版本、模型版本、使用方式和内容哈希。知识更新不会改写历史建议。

## 6. 关系型数据模型

原始文件保存在对象存储；高变化但非关键展示字段可以使用受控 JSON。身份、状态、变量、适用范围、约束、证据和审核等主业务字段必须关系化。

### 6.1 来源与片段

```sql
knowledge_sources(
  source_id uuid primary key,
  project_id uuid not null,
  title text not null,
  source_kind text not null,
  status text not null,
  storage_ref text not null,
  sha256 text not null unique,
  media_type text not null,
  file_name text not null,
  size_bytes bigint not null,
  extraction_status text not null,
  extractor_version text,
  uploaded_by text not null,
  uploaded_at timestamptz not null,
  reviewed_by text,
  reviewed_at timestamptz
)
```

```sql
knowledge_fragments(
  fragment_id uuid primary key,
  source_id uuid not null references knowledge_sources,
  category text not null,
  content text not null,
  page_number integer,
  sheet_name text,
  cell_range text,
  region text,
  content_hash text not null,
  extraction_method text not null,
  extractor_version text not null,
  extraction_confidence double precision,
  human_reviewed boolean not null,
  reviewed_by text,
  reviewed_at timestamptz
)
```

### 6.2 声明与版本

```sql
mechanism_claims(
  claim_id uuid primary key,
  project_id uuid not null,
  current_version integer not null,
  status text not null,
  created_at timestamptz not null,
  updated_at timestamptz not null
)
```

```sql
mechanism_claim_versions(
  claim_id uuid not null references mechanism_claims,
  version integer not null,
  name text not null,
  mechanism_type text not null,
  statement text not null,
  expected_signature text,
  falsification_condition text not null,
  evidence_level text not null,
  created_by text not null,
  created_at timestamptz not null,
  reviewed_by text,
  reviewed_at timestamptz,
  content_hash text not null,
  primary key(claim_id, version)
)
```

### 6.3 变量、适用范围与约束

```sql
mechanism_claim_variables(
  claim_id uuid not null,
  claim_version integer not null,
  variable_code text not null,
  variable_role text not null,
  direction text,
  delay_ms bigint,
  unit text not null,
  primary key(claim_id, claim_version, variable_code, variable_role)
)
```

`variable_code` 必须引用工艺数据模型、控制参数、质量特性或研发项目变量的稳定代码，不允许在机理模块中复制变量定义。

```sql
mechanism_claim_applicability(
  claim_id uuid not null,
  claim_version integer not null,
  dimension_code text not null,
  dimension_value text not null,
  primary key(claim_id, claim_version, dimension_code, dimension_value)
)
```

适用维度首期包括产品系列、产品、材料、设备、工装、场景包、工艺规范和阶段。空适用范围不表示全局适用；它表示范围未完成，不能进入建议引擎。

```sql
mechanism_claim_constraints(
  constraint_id uuid primary key,
  claim_id uuid not null,
  claim_version integer not null,
  variable_code text not null,
  constraint_kind text not null,
  minimum double precision,
  maximum double precision,
  unit text not null,
  severity text not null
)
```

### 6.4 证据、审核与使用

```sql
mechanism_claim_evidence(
  evidence_link_id uuid primary key,
  claim_id uuid not null,
  claim_version integer not null,
  evidence_kind text not null,
  reference_id text not null,
  polarity text not null,
  content_hash text not null,
  created_at timestamptz not null
)
```

```sql
mechanism_claim_reviews(
  review_id uuid primary key,
  claim_id uuid not null,
  claim_version integer not null,
  decision text not null,
  reviewer_id text not null,
  comment text,
  reviewed_at timestamptz not null
)
```

```sql
recommendation_knowledge_usage(
  recommendation_id uuid not null,
  claim_id uuid not null,
  claim_version integer not null,
  usage_type text not null,
  content_hash text not null,
  primary key(recommendation_id, claim_id, claim_version, usage_type)
)
```

## 7. 状态机与治理

### 7.1 知识来源

```text
uploaded → indexed → reviewed → retired
    └──────────────────────────→ retired
```

只有全部正式片段完成人工复核后，来源才能进入 `reviewed`。

### 7.2 机理声明版本

```text
draft → reviewed → supported → validated → active → retired
  └ rejected       └───────────────┴────→ falsified
```

- `draft`：自动提取或人工录入，不能影响建议；
- `reviewed`：结构、变量、单位、来源和适用范围已审核；
- `supported`：至少有一次合格干预结果支持，但不足以声明稳定操作域；
- `validated`：满足预注册的重复、区组、边界或交互验证；
- `active`：被批准用于指定场景的建议能力；
- `rejected`：草稿结构审核未通过；
- `falsified`：正式实验置信区间明确不满足预注册效应，保留审计但立即退出建议；
- `retired`：新版本替代或适用性失效。

创建者不得审核自己的声明或机理模型。审核人不能修改原版本，只能批准、驳回或要求新版本。冲突声明可以并存，系统不得用最后写入覆盖冲突。

## 8. 文档识别与语义提取

识别流水线分成三个独立层，避免一个模型同时负责读取、理解和批准。

### 8.1 确定性结构提取

原生 PDF、Excel、CSV、Markdown 和文本优先使用传统解析器，负责准确读取文本、数值、单位、表格结构、页码和单元格位置。相同文件和提取器版本必须生成相同片段哈希。

### 8.2 OCR 与版面识别

扫描件、现场照片、复杂表格和手写批注使用可替换的专业 OCR/Document AI 适配器。输出必须包含区域、读取顺序和字段级置信度。低置信度数值和单位必须人工确认。

### 8.3 机理语义提取

语言模型只接收已经定位的片段，输出符合固定 Schema 的 `MechanismClaimDraft`。每个字段必须携带来源片段；无法确定的字段返回 `unknown`，不能猜测。

推荐接口边界：

```csharp
public interface IDocumentStructureExtractor;
public interface IMechanismClaimExtractor;
public interface IMechanismClaimReviewService;
public interface IApplicableMechanismKnowledgeProvider;
```

模型供应商、OCR 引擎和部署方式是适配器；正式 Schema、审核状态机和证据引用属于 Platform。

## 9. 服务边界

### Platform

- 保存来源、片段、声明、版本、审核和证据；
- 校验变量、单位、上下文和权限；
- 计算当前建议能力档案；
- 冻结每次建议使用的知识版本；
- 将实验结果连接为支持、反对或验证证据；
- 决定降级、暂停和停止。

### Extraction Worker

- 执行文档结构提取、OCR 和语义草稿生成；
- 无权把草稿变成已审核知识；
- 输出提取器、模型、提示词和 Schema 版本；
- 可在厂内部署或通过受控外部服务调用。

### Optimizer

- 接收已经解析的参数边界、约束、先验和机理特征；
- 不直接检索知识库；
- 不决定知识是否有效；
- 在输入快照相同、策略版本和随机种子相同时可重复执行。

### Agent

- 检索已授权、已审核的知识；
- 帮助工程师形成草稿、解释冲突和生成实验文字；
- 不直接激活知识，不直接生成最终数值设定。

## 10. 建议能力档案

Platform 在调用 Optimizer 前生成并冻结：

```csharp
public sealed record RecommendationCapabilityProfile
{
    public required string Mode { get; init; }
    public bool DataAdmissionPassed { get; init; }
    public bool AllowInterpolation { get; init; }
    public bool AllowExtrapolation { get; init; }
    public IReadOnlyList<ParameterBoundary> HardBoundaries { get; init; } = [];
    public IReadOnlyList<MechanismConstraint> SoftConstraints { get; init; } = [];
    public IReadOnlyList<MechanismClaimReference> ApplicableClaims { get; init; } = [];
    public IReadOnlyList<MechanismModelReference> ActiveModels { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
}
```

能力判定顺序：

1. 检查运行、质量和上下文准入；
2. 合并设备联锁之外的平台硬边界与工艺规范范围；
3. 匹配当前项目和上下文的已激活声明；
4. 检查声明冲突、单位和版本；
5. 匹配可执行机理模型；
6. 选择三种模式之一；
7. 构建 Optimizer 输入；
8. 对输出再次执行确定性边界检查；
9. 保存建议、知识使用记录、模型版本、限制和内容哈希。

存在冲突、范围不明或证据不足时，应排除相关知识或降级模式，不能静默选择一条声明。

## 11. API 提案

### 来源和片段

```text
POST /api/v1/research-projects/{projectId}/knowledge-sources
GET  /api/v1/research-projects/{projectId}/knowledge-sources
GET  /api/v1/knowledge-sources/{sourceId}
POST /api/v1/knowledge-sources/{sourceId}:extract
POST /api/v1/knowledge-fragments/{fragmentId}:review
```

### 机理声明

```text
POST /api/v1/research-projects/{projectId}/mechanism-claims
GET  /api/v1/research-projects/{projectId}/mechanism-claims
GET  /api/v1/mechanism-claims/{claimId}/versions/{version}
POST /api/v1/mechanism-claims/{claimId}/versions/{version}:review
POST /api/v1/mechanism-claims/{claimId}/versions/{version}:activate
POST /api/v1/mechanism-claims/{claimId}/versions/{version}:retire
GET  /api/v1/mechanism-claims:applicable
```

### 建议解释

```text
GET /api/v1/recommendations/{recommendationId}/knowledge-usage
GET /api/v1/recommendations/{recommendationId}/capability-profile
```

写操作必须使用结构化请求和明确的权限策略。状态转换使用命令端点，不能通过通用 PUT 任意覆盖。

## 12. 页面信息架构

### 12.1 工艺研发 / 机理知识

新增稳定入口，包含：

- **知识来源**：上传、解析、哈希、状态和项目范围；
- **提取复核**：原文与提取结果左右对照，点击声明定位页码或单元格；
- **机理声明**：变量映射、方向、阈值、交互、时序、适用范围和反证；
- **关系视图**：原因、媒介、结果和失效模式图；
- **审核队列**：待审核、冲突、低置信度和需要新版本的声明；
- **使用与验证**：被哪些假设、实验、建议和工艺操作域引用。

### 12.2 研发项目

项目工作区增加“机理”页签，只显示当前项目可访问的知识。提出假设时可以引用声明；设计实验时显示该实验将支持或反驳什么；结果完成后展示证据变化，但不自动改写声明。

### 12.3 建议详情

每条建议显示：

- 当前模式；
- 数据范围和样本覆盖；
- 使用的声明及版本；
- 使用方式：约束、先验、特征或解释；
- 预测、不确定性和可行概率；
- 硬安全边界和平台限制；
- 是否位于已有数据范围；
- 建议验证方式和反证条件；
- 工程师采用、修改或拒绝原因。

## 13. 安全、隐私与部署

- 原始工艺资料默认保留在厂内；外部识别服务必须显式配置并记录供应商、区域、模型和数据处理策略；
- 密钥只存在于服务端秘密存储，浏览器不直接调用模型供应商；
- 上传文件执行类型、大小、恶意内容和解压边界检查；
- 提取器运行在资源受限进程，不执行文档宏、脚本或任意代码；
- 每次自动提取记录模型、提示词、Schema 和提取器版本；
- Agent 和 Optimizer 只能读取当前用户、项目和场景授权范围内的知识；
- 已审核版本不可修改，只能创建后继版本或停用；
- 原始来源删除遵循保留策略，不能破坏已发布结论的证据引用。

## 14. 实施顺序与验收

### P0：声明内核和关系型存储

- 增加机理声明、变量、适用范围、证据和审核契约；
- 建立关系型迁移和存储接口；
- 实现创建人与审核人分离；
- 保持 Optimizer 行为不变。

验收：声明可以从来源片段创建、审核、驳回、版本化和定位原文；无知识场景所有既有流程不回归。

### P1：知识工作台和提取适配

- 完成来源上传、提取复核、变量映射和冲突页面；
- 把传统解析、OCR 和语义提取拆成独立接口；
- 为结构化输出增加 Schema 验证和字段级引用。

验收：工程师能从一份代表性文档形成一条完整、可审核、可追溯的声明；错误数字和单位不会自动进入正式知识。

### P2：知识增强建议

- 实现能力档案；
- 先接硬边界、禁止组合、候选空间缩减和解释；
- 保存建议知识使用记录；
- 增加数据模式与知识增强模式的影子对比。

验收：相同输入可重放；知识缺失或失效时确定性降级；不产生超出已知硬边界的建议。

### P3：机理融合

- 接入机理特征、标定、残差模型或集成；
- 冻结模型和融合定义版本；
- 增加校准、漂移和失配停止条件。

验收：历史回放比较数据模式、知识增强模式、机理融合模式与适用简单基线；结果只证明已注册指标，不推广为现场收益。

### P4：前瞻验证

- 在新项目中提前冻结建议和工程师独立选择；
- 记录采用、修改、拒绝原因；
- 比较建议可执行性、校准和无效实验率；
- 通过影子闸门后才申请受控在线实验。

## 15. 评估指标

### 提取质量

- 变量映射准确率；
- 数值与单位准确率；
- 来源定位完整率；
- 自动草稿被接受、修改和驳回的比例；
- 低置信度正确升级人工复核的比例。

### 知识质量

- 有适用范围和反证条件的声明比例；
- 声明冲突检出率；
- 独立审核覆盖率；
- 支持、反对和验证证据完整率；
- 因上下文变化正确拒绝使用的比例。

### 建议价值

- 工程师采用、修改和拒绝比例及原因；
- 建议超范围和已知安全违规数；
- 预测区间覆盖率和可行概率校准；
- 相对 DOE、随机、历史顺序和纯数据模式的实验效率；
- 机理知识带来的候选空间缩减是否误删真实可行区域；
- 跨场景迁移中的负迁移检出率。

## 16. 架构不变量

1. Platform 是正式知识、审核、证据和建议使用记录的唯一事实源。
2. 原始来源、结构化声明、可执行模型和数值建议是四种不同资产，不得互相冒充。
3. 没有机理知识时系统正常运行，但明确降级能力和主张。
4. 语言模型只提出草稿和解释，不批准知识或生成最终数值设定。
5. 正式知识必须带来源、版本、适用范围、反证条件和审核人。
6. 实验结果可以支持、反对或缩小知识范围，但不能静默改写历史版本。
7. Optimizer 不直接访问知识库，只消费 Platform 冻结的结构化输入。
8. 设备安全联锁始终独立于模型和平台约束。

English version: [mechanism-knowledge.en.md](mechanism-knowledge.en.md).
