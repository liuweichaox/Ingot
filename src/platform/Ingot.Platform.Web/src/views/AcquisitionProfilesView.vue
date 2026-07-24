<template>
  <el-card shadow="never" class="catalog-card">
    <template #header>
      <div class="catalog-heading">
        <div><strong>采集任务</strong><span>{{ profiles.length }} 个版本</span></div>
        <el-button type="primary" :icon="Plus" @click="createProfile">新建采集任务</el-button>
      </div>
    </template>

    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <div class="list-toolbar">
      <el-input v-model="keyword" clearable placeholder="搜索任务名称、编码或数据对象" />
      <el-select v-model="statusFilter" clearable placeholder="全部状态">
        <el-option label="草稿" value="draft" />
        <el-option label="已发布" value="published" />
        <el-option label="已停用" value="retired" />
      </el-select>
    </div>
    <el-table v-loading="loading" :data="pagedProfiles" empty-text="暂无采集任务">
      <el-table-column prop="name" label="任务" min-width="180">
        <template #default="{ row }"><strong>{{ row.name }}</strong><div class="secondary">{{ row.profileId }} · v{{ row.version }}</div></template>
      </el-table-column>
      <el-table-column label="采集范围" min-width="205"><template #default="{ row }">{{ row.edgeId }}<div class="secondary">{{ row.subjectType }}/{{ row.subjectId }}</div></template></el-table-column>
      <el-table-column label="工艺数据模型" min-width="170"><template #default="{ row }">{{ row.dataModelId }} · v{{ row.dataModelVersion }}</template></el-table-column>
      <el-table-column label="协议" width="110"><template #default="{ row }">{{ protocolLabel(row.protocol) }}</template></el-table-column>
      <el-table-column label="状态" min-width="145">
        <template #default="{ row }">
          <el-tag :type="statusType(row.status)" effect="plain">{{ statusLabel(row.status) }}</el-tag>
          <template v-if="row.status === 'published'">
            <div class="secondary" :title="runtimeState(row).lastError || ''">{{ runtimeStateLabel(runtimeState(row).state) }}<template v-if="runtimeState(row).samplesCollected !== undefined"> · {{ runtimeState(row).samplesCollected }} 条</template></div>
          </template>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="190">
        <template #default="{ row }">
          <el-button link type="primary" @click="editProfile(row)">{{ row.status === 'draft' ? '编辑' : '查看' }}</el-button>
          <el-button link type="primary" @click="createVersion(row)">新版本</el-button>
          <el-button v-if="row.status === 'draft'" link type="danger" @click="removeProfile(row)">删除</el-button>
          <el-button v-else-if="row.status === 'published'" link type="warning" @click="retireProfile(row)">停用</el-button>
        </template>
      </el-table-column>
    </el-table>
    <TablePagination v-model:page="page" v-model:page-size="pageSize" :total="profileTotal" />
  </el-card>

  <el-drawer v-model="editorVisible" :title="editor.name || '新建采集任务'" size="min(1180px, 96vw)" destroy-on-close>
    <template #header>
      <div class="drawer-heading"><strong>{{ editor.name || "新建采集任务" }}</strong><span>{{ editor.profileId || "尚未填写编码" }} · v{{ editor.version }} · {{ statusLabel(editor.status) }}</span></div>
    </template>

    <el-steps :active="activeStep" simple class="task-steps">
      <el-step title="任务信息" />
      <el-step title="数据源" />
      <el-step title="字段映射" />
      <el-step title="发布检查" />
    </el-steps>

    <el-form label-position="top" class="profile-form">
      <section v-show="activeStep === 0" class="form-section">
        <h3>任务信息</h3>
        <div class="form-grid three">
          <el-form-item label="配置编码"><el-input v-model="editor.profileId" :disabled="!isNewIdentity || !editable" placeholder="例如 furnace-main" /></el-form-item>
          <el-form-item label="版本"><el-input-number v-model="editor.version" :min="1" :disabled="!isNewIdentity || !editable" controls-position="right" /></el-form-item>
          <el-form-item label="名称"><el-input v-model="editor.name" :disabled="!editable" /></el-form-item>
          <el-form-item label="采集节点">
            <el-select v-model="editor.edgeId" :disabled="!editable" filterable placeholder="选择现场采集节点">
              <el-option v-for="edge in edges" :key="edge.edgeId" :label="edge.hostname ? `${edge.edgeId} · ${edge.hostname}` : edge.edgeId" :value="edge.edgeId" />
            </el-select>
          </el-form-item>
          <el-form-item label="工艺数据模型" class="span-two">
            <el-select :model-value="selectedModelKey" :disabled="!editable" filterable placeholder="选择已发布数据模型" @change="selectModel">
              <el-option v-for="model in publishedModels" :key="modelKey(model)" :label="`${model.name} · v${model.version}`" :value="modelKey(model)" />
            </el-select>
          </el-form-item>
        </div>
      </section>

      <section v-show="activeStep === 1" class="form-section">
        <h3>数据源与采集对象</h3>
        <div class="form-grid two">
          <el-form-item label="采集协议">
            <el-select v-model="editor.protocol" :disabled="!editable" @change="changeProtocol">
              <el-option v-for="option in protocolOptions" :key="option.value" :label="option.label" :value="option.value" />
            </el-select>
          </el-form-item>
          <el-form-item label="连接超时"><el-input-number v-model="editor.execution.timeoutMs" :min="100" :step="100" :disabled="!editable" controls-position="right" /><span class="unit">毫秒</span></el-form-item>

          <template v-if="editor.protocol === 'http-polling'">
            <el-form-item label="采集周期"><el-input-number v-model="editor.connection.pollIntervalMs" :min="100" :step="100" :disabled="!editable" controls-position="right" /><span class="unit">毫秒</span></el-form-item>
            <el-form-item label="设备服务地址"><el-input v-model="editor.connection.baseUrl" :disabled="!editable" placeholder="http://设备地址:端口" /></el-form-item>
            <el-form-item label="数据接口路径"><el-input v-model="editor.connection.snapshotPath" :disabled="!editable" /></el-form-item>
            <el-form-item label="时间戳来源"><el-select v-model="editor.timestampMode" :disabled="!editable"><el-option label="源数据时间" value="source" /><el-option label="边缘接收时间" value="edge-received" /></el-select></el-form-item>
            <el-form-item v-if="editor.timestampMode === 'source'" label="时间戳字段路径"><el-input v-model="editor.timestampPath" :disabled="!editable" /></el-form-item>
          </template>

          <template v-else-if="editor.protocol === 'mqtt'">
            <el-form-item label="Broker 主机"><el-input v-model="editor.mqtt.host" :disabled="!editable" placeholder="设备或消息服务地址" /></el-form-item>
            <el-form-item label="端口"><el-input-number v-model="editor.mqtt.port" :min="1" :max="65535" :disabled="!editable" controls-position="right" /></el-form-item>
            <el-form-item label="协议版本"><el-select v-model="editor.mqtt.protocolVersion" :disabled="!editable"><el-option label="MQTT 5.0" value="5.0" /><el-option label="MQTT 3.1.1" value="3.1.1" /></el-select></el-form-item>
            <el-form-item label="客户端 ID"><el-input v-model="editor.mqtt.clientId" :disabled="!editable" placeholder="留空时由边缘节点生成" /></el-form-item>
            <el-form-item label="用户名"><el-input v-model="editor.mqtt.username" :disabled="!editable" /></el-form-item>
            <el-form-item label="密码凭据引用"><el-input v-model="editor.mqtt.passwordSecretRef" :disabled="!editable" placeholder="env:MQTT_PASSWORD" /></el-form-item>
            <el-form-item label="连接选项"><div class="inline-options"><el-switch v-model="editor.mqtt.useTls" :disabled="!editable" active-text="TLS" /><el-switch v-model="editor.mqtt.cleanSession" :disabled="!editable" active-text="清理会话" /></div></el-form-item>
            <el-form-item label="保活时间"><el-input-number v-model="editor.mqtt.keepAliveSeconds" :min="5" :disabled="!editable" controls-position="right" /><span class="unit">秒</span></el-form-item>
            <template v-if="editor.mqtt.useTls">
              <el-form-item label="CA 证书路径"><el-input v-model="editor.mqtt.caCertificatePath" :disabled="!editable" placeholder="使用系统信任链时可留空" /></el-form-item>
              <el-form-item label="客户端证书路径"><el-input v-model="editor.mqtt.clientCertificatePath" :disabled="!editable" placeholder="不使用双向 TLS 时可留空" /></el-form-item>
              <el-form-item label="证书密码凭据引用"><el-input v-model="editor.mqtt.clientCertificatePasswordSecretRef" :disabled="!editable" placeholder="env:MQTT_CERT_PASSWORD" /></el-form-item>
            </template>
            <el-form-item label="时间戳来源"><el-select v-model="editor.timestampMode" :disabled="!editable"><el-option label="源数据时间" value="source" /><el-option label="边缘接收时间" value="edge-received" /></el-select></el-form-item>
            <el-form-item v-if="editor.timestampMode === 'source'" label="时间戳字段路径"><el-input v-model="editor.timestampPath" :disabled="!editable" /></el-form-item>
            <el-form-item label="序号字段路径"><el-input v-model="editor.sequencePath" :disabled="!editable" placeholder="可留空" /></el-form-item>
            <div class="span-two topic-editor">
              <div class="section-title"><h4>订阅主题</h4><el-button v-if="editable" link type="primary" :icon="Plus" @click="editor.mqtt.topics.push({ topic: '', qos: 0 })">添加</el-button></div>
              <div v-for="(topic, index) in editor.mqtt.topics" :key="index" class="topic-row">
                <el-input v-model="topic.topic" :disabled="!editable" placeholder="例如 factory/line/+/telemetry" />
                <el-select v-model="topic.qos" :disabled="!editable"><el-option label="QoS 0" :value="0" /><el-option label="QoS 1" :value="1" /><el-option label="QoS 2" :value="2" /></el-select>
                <el-button v-if="editable" link type="danger" @click="editor.mqtt.topics.splice(index, 1)">删除</el-button>
              </div>
            </div>
          </template>

          <template v-else-if="editor.protocol === 'opc-ua'">
            <el-form-item label="服务器端点" class="span-two"><el-input v-model="editor.opcUa.endpointUrl" :disabled="!editable" placeholder="opc.tcp://设备地址:4840" /></el-form-item>
            <el-form-item label="消息安全模式"><el-select v-model="editor.opcUa.securityMode" :disabled="!editable"><el-option label="无" value="none" /><el-option label="签名" value="sign" /><el-option label="签名并加密" value="sign-and-encrypt" /></el-select></el-form-item>
            <el-form-item label="安全策略"><el-select v-model="editor.opcUa.securityPolicy" :disabled="!editable"><el-option label="None" value="None" /><el-option label="Basic256Sha256" value="Basic256Sha256" /><el-option label="Aes128_Sha256_RsaOaep" value="Aes128_Sha256_RsaOaep" /><el-option label="Aes256_Sha256_RsaPss" value="Aes256_Sha256_RsaPss" /></el-select></el-form-item>
            <el-form-item label="身份认证"><el-select v-model="editor.opcUa.authenticationType" :disabled="!editable"><el-option label="匿名" value="anonymous" /><el-option label="用户名" value="username" /><el-option label="证书" value="certificate" /></el-select></el-form-item>
            <el-form-item v-if="editor.opcUa.authenticationType === 'username'" label="用户名"><el-input v-model="editor.opcUa.username" :disabled="!editable" /></el-form-item>
            <el-form-item v-if="editor.opcUa.authenticationType === 'username'" label="密码凭据引用"><el-input v-model="editor.opcUa.passwordSecretRef" :disabled="!editable" placeholder="env:OPCUA_PASSWORD" /></el-form-item>
            <el-form-item v-if="editor.opcUa.authenticationType === 'certificate'" label="客户端证书路径"><el-input v-model="editor.opcUa.clientCertificatePath" :disabled="!editable" /></el-form-item>
            <el-form-item v-if="editor.opcUa.authenticationType === 'certificate'" label="证书密码凭据引用"><el-input v-model="editor.opcUa.clientCertificatePasswordSecretRef" :disabled="!editable" placeholder="env:OPCUA_CERT_PASSWORD" /></el-form-item>
            <el-form-item label="发布周期"><el-input-number v-model="editor.opcUa.publishingIntervalMs" :min="100" :step="100" :disabled="!editable" controls-position="right" /><span class="unit">毫秒</span></el-form-item>
            <el-form-item label="采样周期"><el-input-number v-model="editor.opcUa.samplingIntervalMs" :min="100" :step="100" :disabled="!editable" controls-position="right" /><span class="unit">毫秒</span></el-form-item>
            <el-form-item label="信任服务器证书"><el-switch v-model="editor.opcUa.trustServerCertificate" :disabled="!editable" /></el-form-item>
          </template>

          <template v-else>
            <el-form-item label="设备主机"><el-input v-model="editor.modbusTcp.host" :disabled="!editable" /></el-form-item>
            <el-form-item label="端口"><el-input-number v-model="editor.modbusTcp.port" :min="1" :max="65535" :disabled="!editable" controls-position="right" /></el-form-item>
            <el-form-item label="单元 ID"><el-input-number v-model="editor.modbusTcp.unitId" :min="0" :max="255" :disabled="!editable" controls-position="right" /></el-form-item>
            <el-form-item label="采集周期"><el-input-number v-model="editor.modbusTcp.pollIntervalMs" :min="100" :step="100" :disabled="!editable" controls-position="right" /><span class="unit">毫秒</span></el-form-item>
          </template>

          <el-form-item label="对象类型"><el-input v-model="editor.subjectType" :disabled="!editable" placeholder="equipment" /></el-form-item>
          <el-form-item label="对象编号"><el-input v-model="editor.subjectId" :disabled="!editable" placeholder="设备唯一编号" /></el-form-item>
          <el-form-item label="事件来源"><el-input v-model="editor.source" :disabled="!editable" :placeholder="`connector/${editor.protocol}`" /></el-form-item>
          <el-form-item label="采样事件类型"><el-input v-model="editor.sampleEventType" :disabled="!editable" /></el-form-item>
          <el-form-item label="重连间隔"><el-input-number v-model="editor.execution.reconnectDelayMs" :min="100" :step="100" :disabled="!editable" controls-position="right" /><span class="unit">毫秒</span></el-form-item>
        </div>
      </section>

      <div v-show="activeStep === 2" class="mapping-sections">
        <section class="form-section">
          <div class="section-title"><h3>采集字段映射</h3><span>数据类型和单位继承工艺数据模型</span></div>
          <el-table :data="mappingRows" size="small" empty-text="请先选择工艺数据模型">
            <el-table-column label="采集" width="70"><template #default="{ row }"><el-checkbox v-model="row.enabled" :disabled="!editable || !row.nullable" @change="updateValueMappings" /></template></el-table-column>
            <el-table-column prop="sourceField" label="数据项" min-width="170"><template #default="{ row }"><strong>{{ row.sourceField }}</strong><div class="secondary">{{ row.code }}</div></template></el-table-column>
            <el-table-column label="类型 / 单位" width="130"><template #default="{ row }">{{ row.dataType }}<span v-if="row.unit"> · {{ row.unit }}</span></template></el-table-column>
            <el-table-column :label="sourceSelectorLabel" min-width="270">
              <template #default="{ row }">
                <div v-if="editor.protocol === 'modbus-tcp'" class="modbus-mapping">
                  <el-select v-model="row.modbusArea" :disabled="!editable || !row.enabled" @change="updateValueMappings">
                    <el-option label="保持寄存器" value="holding-register" /><el-option label="输入寄存器" value="input-register" />
                    <el-option label="线圈" value="coil" /><el-option label="离散输入" value="discrete-input" />
                  </el-select>
                  <el-input-number v-model="row.modbusAddress" :min="0" :max="65535" :disabled="!editable || !row.enabled" controls-position="right" @change="updateValueMappings" />
                  <el-select v-model="row.sourceDataType" :disabled="!editable || !row.enabled" @change="syncModbusQuantity(row)">
                    <el-option v-for="type in modbusDataTypes" :key="type.value" :label="type.label" :value="type.value" />
                  </el-select>
                  <el-select v-if="row.modbusQuantity > 1" v-model="row.wordOrder" :disabled="!editable || !row.enabled" @change="updateValueMappings">
                    <el-option label="高字在前" value="high-low" /><el-option label="低字在前" value="low-high" />
                  </el-select>
                  <el-select v-model="row.byteOrder" :disabled="!editable || !row.enabled" @change="updateValueMappings">
                    <el-option label="大端字节" value="big-endian" /><el-option label="小端字节" value="little-endian" />
                  </el-select>
                </div>
                <el-input v-else v-model="row.sourcePath" :disabled="!editable || !row.enabled" :placeholder="sourceSelectorPlaceholder" @input="updateValueMappings" />
              </template>
            </el-table-column>
            <el-table-column label="换算" width="180"><template #default="{ row }"><div class="transform-row"><el-input-number v-model="row.scale" :disabled="!editable || !row.enabled" :controls="false" placeholder="倍率" @change="updateValueMappings" /><el-input-number v-model="row.offset" :disabled="!editable || !row.enabled" :controls="false" placeholder="偏移" @change="updateValueMappings" /></div></template></el-table-column>
            <el-table-column label="必填" width="70"><template #default="{ row }"><el-checkbox v-model="row.required" :disabled="!editable || !row.enabled || !row.nullable" @change="updateValueMappings" /></template></el-table-column>
          </el-table>
        </section>

        <section class="form-section">
          <div class="section-title"><h3>运行上下文映射</h3><el-button v-if="editable" link type="primary" :icon="Plus" @click="addContextMapping">添加</el-button></div>
          <el-table :data="editor.contextMappings" size="small" empty-text="没有需要从设备读取的运行上下文">
            <el-table-column label="上下文键" min-width="180"><template #default="{ row }"><el-input v-model="row.contextKey" :disabled="!editable" placeholder="例如 product_code" /></template></el-table-column>
            <el-table-column :label="contextSelectorLabel" min-width="280"><template #default="{ row }"><el-input v-model="row.sourcePath" :disabled="!editable" :placeholder="contextSelectorPlaceholder" /></template></el-table-column>
            <el-table-column label="必填" width="70"><template #default="{ row }"><el-checkbox v-model="row.required" :disabled="!editable" /></template></el-table-column>
            <el-table-column v-if="editable" width="60"><template #default="{ $index }"><el-button link type="danger" @click="editor.contextMappings.splice($index, 1)">删除</el-button></template></el-table-column>
          </el-table>
        </section>

        <section class="form-section">
          <div class="section-title"><h3>运行边界</h3><el-switch v-model="lifecycleEnabled" :disabled="!editable" @change="toggleLifecycle" /></div>
          <div v-if="editor.lifecycle" class="form-grid two">
            <el-form-item label="周期关联号上下文键"><el-input v-model="editor.lifecycle.correlationIdContextKey" :disabled="!editable" /></el-form-item>
            <el-form-item label="预计周期时长"><el-input-number v-model="editor.lifecycle.expectedDurationMs" :min="1" :disabled="!editable" controls-position="right" /><span class="unit">毫秒</span></el-form-item>
            <el-form-item label="控制器步序上下文键"><el-input v-model="editor.lifecycle.stepContextKey" :disabled="!editable" placeholder="可留空" /></el-form-item>
            <el-form-item label="控制器步序名称键"><el-input v-model="editor.lifecycle.stepNameContextKey" :disabled="!editable" placeholder="可留空" /></el-form-item>
          </div>
        </section>

        <section v-if="selectedModel?.recipeParameters?.length" class="form-section">
          <div class="section-title"><h3>配方采集</h3><el-switch v-model="recipeEnabled" :disabled="!editable" @change="toggleRecipe" /></div>
          <template v-if="editor.recipe">
            <div class="form-grid two">
              <el-form-item label="配方编号来源"><el-input v-model="editor.recipe.idPath" :disabled="!editable" :placeholder="contextSelectorPlaceholder" /></el-form-item>
              <el-form-item label="配方版本来源"><el-input v-model="editor.recipe.versionPath" :disabled="!editable" :placeholder="contextSelectorPlaceholder" /></el-form-item>
              <el-form-item label="配方名称来源"><el-input v-model="editor.recipe.namePath" :disabled="!editable" placeholder="可留空" /></el-form-item>
              <el-form-item v-if="jsonPayloadProtocol" label="配方参数对象路径"><el-input v-model="editor.recipe.parametersPath" :disabled="!editable" /></el-form-item>
            </div>
            <el-table :data="recipeMappingRows" size="small">
              <el-table-column label="采集" width="70"><template #default="{ row }"><el-checkbox v-model="row.enabled" :disabled="!editable || !row.nullable" @change="updateRecipeMappings" /></template></el-table-column>
              <el-table-column label="配方参数" min-width="170"><template #default="{ row }"><strong>{{ row.sourceField }}</strong><div class="secondary">{{ row.code }}</div></template></el-table-column>
              <el-table-column :label="sourceSelectorLabel" min-width="270">
                <template #default="{ row }">
                  <div v-if="editor.protocol === 'modbus-tcp'" class="modbus-mapping">
                    <el-select v-model="row.modbusArea" :disabled="!editable || !row.enabled" @change="updateRecipeMappings"><el-option label="保持寄存器" value="holding-register" /><el-option label="输入寄存器" value="input-register" /></el-select>
                    <el-input-number v-model="row.modbusAddress" :min="0" :max="65535" :disabled="!editable || !row.enabled" controls-position="right" @change="updateRecipeMappings" />
                    <el-select v-model="row.sourceDataType" :disabled="!editable || !row.enabled" @change="syncRecipeModbusQuantity(row)"><el-option v-for="type in modbusDataTypes" :key="type.value" :label="type.label" :value="type.value" /></el-select>
                  </div>
                  <el-input v-else v-model="row.sourcePath" :disabled="!editable || !row.enabled" @input="updateRecipeMappings" />
                </template>
              </el-table-column>
              <el-table-column label="必填" width="70"><template #default="{ row }"><el-checkbox v-model="row.required" :disabled="!editable || !row.enabled || !row.nullable" @change="updateRecipeMappings" /></template></el-table-column>
            </el-table>
          </template>
        </section>
      </div>

      <section v-show="activeStep === 3" class="form-section review-section">
        <h3>发布检查</h3>
        <el-descriptions :column="2" border>
          <el-descriptions-item label="采集任务">{{ editor.name || "-" }}</el-descriptions-item>
          <el-descriptions-item label="采集节点">{{ editor.edgeId || "-" }}</el-descriptions-item>
          <el-descriptions-item label="工艺数据模型">{{ selectedModel ? `${selectedModel.name} · v${selectedModel.version}` : "-" }}</el-descriptions-item>
          <el-descriptions-item label="数据对象">{{ editor.subjectId ? `${editor.subjectType}/${editor.subjectId}` : "-" }}</el-descriptions-item>
          <el-descriptions-item label="数据源">{{ sourceEndpoint || "-" }}</el-descriptions-item>
          <el-descriptions-item label="采集协议">{{ protocolLabel(editor.protocol) }}</el-descriptions-item>
          <el-descriptions-item label="采集周期">{{ typeof acquisitionInterval === "number" ? `${acquisitionInterval} 毫秒` : acquisitionInterval }}</el-descriptions-item>
          <el-descriptions-item label="数据项映射">{{ editor.valueMappings.length }} 项</el-descriptions-item>
          <el-descriptions-item label="运行上下文">{{ editor.contextMappings.length }} 项</el-descriptions-item>
          <el-descriptions-item label="运行边界">{{ editor.lifecycle ? "离散周期" : "连续运行" }}</el-descriptions-item>
          <el-descriptions-item label="配方采集">{{ editor.recipe ? `${editor.recipe.parameterMappings.length} 项参数` : "未启用" }}</el-descriptions-item>
          <el-descriptions-item label="发布版本">{{ editor.profileId || "-" }} · v{{ editor.version }}</el-descriptions-item>
        </el-descriptions>
        <div v-if="validationIssues.length" class="validation-panel">
          <strong>还需完成 {{ validationIssues.length }} 项</strong>
          <ul><li v-for="issue in validationIssues" :key="issue">{{ issue }}</li></ul>
        </div>
        <div v-else class="validation-ready">
          <el-icon><CircleCheckFilled /></el-icon>
          <div><strong>配置检查通过</strong><span>发布后将由所选采集节点加载此版本</span></div>
        </div>
      </section>
    </el-form>

    <template #footer>
      <div class="drawer-actions">
        <el-button @click="editorVisible = false">关闭</el-button>
        <el-button v-if="activeStep > 0" @click="activeStep -= 1">上一步</el-button>
        <el-button v-if="activeStep < 3" type="primary" @click="nextStep">下一步</el-button>
        <template v-if="editable">
          <el-button :loading="saving" @click="save('draft')">保存草稿</el-button>
          <el-button v-if="activeStep === 3" type="primary" :loading="saving" :disabled="validationIssues.length > 0" @click="save('published')">发布任务</el-button>
        </template>
      </div>
    </template>
  </el-drawer>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from "vue";
import { useRoute } from "vue-router";
import { ElCheckbox, ElMessage, ElMessageBox } from "element-plus";
import { CircleCheckFilled, Plus } from "@element-plus/icons-vue";
import { deleteJson, getJson, postJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

const profiles = ref([]);
const models = ref([]);
const edges = ref([]);
const loading = ref(false);
const saving = ref(false);
const error = ref("");
const editorVisible = ref(false);
const isNewIdentity = ref(true);
const activeStep = ref(0);
const route = useRoute();
const keyword = ref(typeof route.query.edgeId === "string" ? route.query.edgeId : "");
const statusFilter = ref("");
const mappingRows = ref([]);
const recipeMappingRows = ref([]);
const recipeEnabled = ref(false);
const lifecycleEnabled = ref(false);
const runtimeTasks = ref({});
const unavailableEdges = ref(new Set());
const editor = reactive(emptyProfile());
let runtimeTimer;
const protocolOptions = [
  { value: "http-polling", label: "HTTP 轮询" },
  { value: "mqtt", label: "MQTT" },
  { value: "opc-ua", label: "OPC UA" },
  { value: "modbus-tcp", label: "Modbus TCP" },
];
const modbusDataTypes = [
  { value: "uint16", label: "UInt16" }, { value: "int16", label: "Int16" },
  { value: "uint32", label: "UInt32" }, { value: "int32", label: "Int32" },
  { value: "float32", label: "Float32" }, { value: "uint64", label: "UInt64" },
  { value: "int64", label: "Int64" }, { value: "float64", label: "Float64" },
  { value: "string", label: "String" },
];

const publishedModels = computed(() => models.value.filter(item => item.status === "published"));
const filteredProfiles = computed(() => {
  const term = keyword.value.trim().toLowerCase();
  return profiles.value.filter(item => {
    const matchesStatus = !statusFilter.value || item.status === statusFilter.value;
    const searchable = `${item.name} ${item.profileId} ${item.subjectType} ${item.subjectId} ${item.edgeId}`.toLowerCase();
    return matchesStatus && (!term || searchable.includes(term));
  });
});
const { page, pageSize, total: profileTotal, pagedItems: pagedProfiles, resetPage } = useClientPagination(filteredProfiles);
const selectedModelKey = computed(() => editor.dataModelId ? `${editor.dataModelId}@${editor.dataModelVersion}` : "");
const selectedModel = computed(() => models.value.find(item => modelKey(item) === selectedModelKey.value));
const editable = computed(() => editor.status === "draft");
const jsonPayloadProtocol = computed(() => ["http-polling", "mqtt"].includes(editor.protocol));
const sourceSelectorLabel = computed(() => ({
  "http-polling": "JSON 字段路径", mqtt: "消息字段路径", "opc-ua": "NodeId", "modbus-tcp": "寄存器来源",
})[editor.protocol]);
const sourceSelectorPlaceholder = computed(() => ({
  "http-polling": "例如 sensors.temperature", mqtt: "例如 payload.temperature", "opc-ua": "例如 ns=2;s=Machine.Temperature",
})[editor.protocol] || "");
const contextSelectorLabel = computed(() => ({
  "http-polling": "JSON 字段路径", mqtt: "消息字段路径", "opc-ua": "NodeId", "modbus-tcp": "寄存器选择器",
})[editor.protocol]);
const contextSelectorPlaceholder = computed(() => ({
  "http-polling": "例如 cycle.id", mqtt: "例如 cycle.id", "opc-ua": "例如 ns=2;s=Cycle.Id",
  "modbus-tcp": "例如 holding-register:200:uint32",
})[editor.protocol]);
const sourceEndpoint = computed(() => {
  if (editor.protocol === "http-polling") {
    const base = editor.connection.baseUrl?.replace(/\/+$/, "");
    const path = editor.connection.snapshotPath?.startsWith("/") ? editor.connection.snapshotPath : `/${editor.connection.snapshotPath || ""}`;
    return base ? `${base}${path}` : "";
  }
  if (editor.protocol === "mqtt") return editor.mqtt.host ? `${editor.mqtt.host}:${editor.mqtt.port}` : "";
  if (editor.protocol === "opc-ua") return editor.opcUa.endpointUrl;
  return editor.modbusTcp.host ? `${editor.modbusTcp.host}:${editor.modbusTcp.port} · Unit ${editor.modbusTcp.unitId}` : "";
});
const acquisitionInterval = computed(() => ({
  "http-polling": editor.connection.pollIntervalMs,
  mqtt: "消息驱动",
  "opc-ua": editor.opcUa.samplingIntervalMs,
  "modbus-tcp": editor.modbusTcp.pollIntervalMs,
})[editor.protocol]);
const validationIssues = computed(() => [
  ...stepIssues(0),
  ...stepIssues(1),
  ...stepIssues(2),
]);
watch([keyword, statusFilter], resetPage);

function emptyProfile() {
  return {
    profileId: "", version: 1, name: "", status: "draft", edgeId: "", protocol: "http-polling",
    dataModelId: "", dataModelVersion: 1, source: "connector/http-polling", subjectType: "equipment", subjectId: "",
    connection: { baseUrl: "", snapshotPath: "/api/v1/snapshot", pollIntervalMs: 1000 },
    mqtt: { host: "", port: 1883, protocolVersion: "5.0", clientId: "", username: "", passwordSecretRef: "", useTls: false, caCertificatePath: "", clientCertificatePath: "", clientCertificatePasswordSecretRef: "", cleanSession: true, keepAliveSeconds: 30, topics: [{ topic: "", qos: 0 }] },
    opcUa: { endpointUrl: "", securityMode: "none", securityPolicy: "None", authenticationType: "anonymous", username: "", passwordSecretRef: "", clientCertificatePath: "", clientCertificatePasswordSecretRef: "", trustServerCertificate: false, publishingIntervalMs: 1000, samplingIntervalMs: 1000 },
    modbusTcp: { host: "", port: 502, unitId: 1, pollIntervalMs: 1000 },
    execution: { timeoutMs: 10000, reconnectDelayMs: 5000 },
    timestampMode: "source", timestampPath: "timestamp", sequencePath: "sequence", sampleEventType: "process.sample",
    staticContext: {}, contextMappings: [], valueMappings: [], recipe: null, lifecycle: null,
  };
}

function replaceEditor(value) {
  const defaults = emptyProfile();
  const incoming = clonePlain(value);
  Object.assign(editor, defaults, incoming, {
    connection: { ...defaults.connection, ...(incoming.connection || {}) },
    mqtt: { ...defaults.mqtt, ...(incoming.mqtt || {}), topics: incoming.mqtt?.topics || defaults.mqtt.topics },
    opcUa: { ...defaults.opcUa, ...(incoming.opcUa || {}) },
    modbusTcp: { ...defaults.modbusTcp, ...(incoming.modbusTcp || {}) },
    execution: { ...defaults.execution, ...(incoming.execution || {}) },
  });
  activeStep.value = 0;
  recipeEnabled.value = Boolean(editor.recipe);
  lifecycleEnabled.value = Boolean(editor.lifecycle);
  rebuildMappings();
}

function modelKey(model) { return `${model.modelId}@${model.version}`; }
function protocolLabel(protocol) { return protocolOptions.find(item => item.value === protocol)?.label || protocol; }
function statusLabel(status) { return ({ draft: "草稿", published: "已发布", retired: "已停用" })[status] || status; }
function statusType(status) { return ({ published: "success", retired: "info", draft: "warning" })[status] || "info"; }

async function load() {
  loading.value = true; error.value = "";
  try {
    const [profilePayload, modelPayload, edgePayload] = await Promise.all([
      getJson("/api/v1/acquisition-profiles"), getJson("/api/v1/process-data-models"), getJson("/api/edges"),
    ]);
    profiles.value = profilePayload.data || [];
    models.value = modelPayload.data || [];
    edges.value = Array.isArray(edgePayload) ? edgePayload : edgePayload.data || [];
    await loadRuntimeStatuses();
  } catch (cause) { error.value = cause.message; }
  finally { loading.value = false; }
}

async function loadRuntimeStatuses() {
  const nextTasks = {};
  const unavailable = new Set();
  await Promise.all(edges.value.map(async edge => {
    try {
      const payload = await getJson(`/api/edges/${encodeURIComponent(edge.edgeId)}/acquisition/status`);
      for (const task of payload.tasks || [])
        nextTasks[`${edge.edgeId}|${task.configurationKey}`] = task;
    } catch {
      unavailable.add(edge.edgeId);
    }
  }));
  runtimeTasks.value = nextTasks;
  unavailableEdges.value = unavailable;
}

function runtimeState(row) {
  const task = runtimeTasks.value[`${row.edgeId}|${row.profileId}@${row.version}`];
  if (task) return task;
  return { state: unavailableEdges.value.has(row.edgeId) ? "unreachable" : "not-loaded" };
}
function runtimeStateLabel(state) {
  return ({ running: "运行中", degraded: "异常", starting: "启动中", unreachable: "节点不可达", "not-loaded": "未加载" })[state] || state;
}
function createProfile() {
  isNewIdentity.value = true; replaceEditor(emptyProfile()); editorVisible.value = true;
}
function editProfile(row) {
  isNewIdentity.value = false; replaceEditor(row); editorVisible.value = true;
}
function createVersion(row) {
  const next = Math.max(0, ...profiles.value.filter(item => item.profileId === row.profileId).map(item => item.version)) + 1;
  isNewIdentity.value = true; replaceEditor({ ...clonePlain(row), version: next, status: "draft" }); editorVisible.value = true;
}

function changeProtocol(protocol) {
  editor.source = `connector/${protocol}`;
  editor.valueMappings = [];
  rebuildMappings();
}

function selectModel(key) {
  const model = models.value.find(item => modelKey(item) === key);
  if (!model) return;
  editor.dataModelId = model.modelId; editor.dataModelVersion = model.version;
  rebuildMappings();
}

function rebuildMappings() {
  const model = selectedModel.value;
  if (!model) { mappingRows.value = []; recipeMappingRows.value = []; return; }
  const existing = new Map((editor.valueMappings || []).map(item => [item.dataItemCode, item]));
  mappingRows.value = model.acquisition.dataItems.map(item => {
    const mapping = existing.get(item.code);
    return {
      ...item,
      enabled: Boolean(mapping) || !item.nullable,
      sourcePath: mapping?.sourcePath || (editor.protocol === "opc-ua" ? "" : item.sourceField),
      required: mapping?.required ?? !item.nullable,
      sourceDataType: mapping?.sourceDataType || "uint16",
      scale: mapping?.scale ?? 1,
      offset: mapping?.offset ?? 0,
      modbusArea: mapping?.modbusArea || "holding-register",
      modbusAddress: mapping?.modbusAddress ?? null,
      modbusQuantity: mapping?.modbusQuantity ?? 1,
      byteOrder: mapping?.byteOrder || "big-endian",
      wordOrder: mapping?.wordOrder || "high-low",
    };
  });
  updateValueMappings();
  const recipeExisting = new Map((editor.recipe?.parameterMappings || []).map(item => [item.dataItemCode, item]));
  recipeMappingRows.value = (model.recipeParameters || []).map(item => {
    const mapping = recipeExisting.get(item.code);
    return {
      ...item,
      enabled: Boolean(mapping) || !item.nullable,
      sourcePath: mapping?.sourcePath || item.sourceField,
      required: mapping?.required ?? !item.nullable,
      sourceDataType: mapping?.sourceDataType || "float64",
      modbusArea: mapping?.modbusArea || "holding-register",
      modbusAddress: mapping?.modbusAddress ?? null,
      modbusQuantity: mapping?.modbusQuantity ?? 4,
      byteOrder: mapping?.byteOrder || "big-endian",
      wordOrder: mapping?.wordOrder || "high-low",
    };
  });
  if (editor.recipe) updateRecipeMappings();
}

function updateValueMappings() {
  editor.valueMappings = mappingRows.value.filter(row => row.enabled).map(row => ({
    dataItemCode: row.code,
    sourcePath: editor.protocol === "modbus-tcp" ? `${row.modbusArea}:${row.modbusAddress}` : row.sourcePath,
    required: row.required,
    sourceDataType: row.sourceDataType,
    scale: row.scale,
    offset: row.offset,
    modbusArea: editor.protocol === "modbus-tcp" ? row.modbusArea : null,
    modbusAddress: editor.protocol === "modbus-tcp" ? row.modbusAddress : null,
    modbusQuantity: editor.protocol === "modbus-tcp" ? row.modbusQuantity : 1,
    byteOrder: row.byteOrder,
    wordOrder: row.wordOrder,
  }));
}
function syncModbusQuantity(row) {
  row.modbusQuantity = ["uint16", "int16"].includes(row.sourceDataType) ? 1 : ["uint32", "int32", "float32"].includes(row.sourceDataType) ? 2 : ["string"].includes(row.sourceDataType) ? 8 : 4;
  updateValueMappings();
}
function updateRecipeMappings() {
  if (!editor.recipe) return;
  editor.recipe.parameterMappings = recipeMappingRows.value.filter(row => row.enabled).map(row => ({
    dataItemCode: row.code,
    sourcePath: editor.protocol === "modbus-tcp" ? `${row.modbusArea}:${row.modbusAddress}` : row.sourcePath,
    required: row.required,
    sourceDataType: row.sourceDataType,
    modbusArea: editor.protocol === "modbus-tcp" ? row.modbusArea : null,
    modbusAddress: editor.protocol === "modbus-tcp" ? row.modbusAddress : null,
    modbusQuantity: editor.protocol === "modbus-tcp" ? row.modbusQuantity : 1,
    byteOrder: row.byteOrder,
    wordOrder: row.wordOrder,
  }));
}
function syncRecipeModbusQuantity(row) {
  row.modbusQuantity = ["uint16", "int16"].includes(row.sourceDataType) ? 1 : ["uint32", "int32", "float32"].includes(row.sourceDataType) ? 2 : ["string"].includes(row.sourceDataType) ? 8 : 4;
  updateRecipeMappings();
}
function toggleRecipe(enabled) {
  editor.recipe = enabled ? { eventType: "recipe.applied", idPath: "recipe.id", versionPath: "recipe.version", namePath: "recipe.name", parametersPath: "recipe.parameters", parameterMappings: [] } : null;
  if (enabled) updateRecipeMappings();
}
function toggleLifecycle(enabled) {
  editor.lifecycle = enabled ? {
    mode: "discrete-cycle",
    correlationIdContextKey: "correlation_id",
    stepContextKey: "recipe_step",
    stepNameContextKey: "recipe_step_name",
    startedEventType: "cycle.started",
    completedEventType: "cycle.completed",
    stepChangedEventType: "recipe.step_changed",
    expectedDurationMs: 600000,
  } : null;
}
function addContextMapping() { editor.contextMappings.push({ contextKey: "", sourcePath: "", required: false }); }
function clonePlain(value) { return JSON.parse(JSON.stringify(value)); }

function stepIssues(step) {
  if (step === 0) {
    return [
      !editor.profileId.trim() && "填写任务编码",
      !editor.name.trim() && "填写任务名称",
      !editor.edgeId && "选择采集节点",
      !editor.dataModelId && "选择工艺数据模型",
    ].filter(Boolean);
  }
  if (step === 1) {
    const issues = [
      !editor.subjectType.trim() && "填写对象类型",
      !editor.subjectId.trim() && "填写对象编号",
      !editor.source.trim() && "填写事件来源",
      !editor.sampleEventType.trim() && "填写采样事件类型",
      editor.execution.timeoutMs < 100 && "连接超时不能小于 100 毫秒",
      editor.execution.reconnectDelayMs < 100 && "重连间隔不能小于 100 毫秒",
    ];
    if (editor.protocol === "http-polling") {
      let validUrl = false;
      try { validUrl = ["http:", "https:"].includes(new URL(editor.connection.baseUrl).protocol); } catch { validUrl = false; }
      issues.push(!validUrl && "填写有效的 HTTP 或 HTTPS 设备服务地址", !editor.connection.snapshotPath.trim() && "填写数据接口路径", editor.connection.pollIntervalMs < 100 && "采集周期不能小于 100 毫秒", editor.timestampMode === "source" && !editor.timestampPath.trim() && "填写时间戳字段路径");
    } else if (editor.protocol === "mqtt") {
      issues.push(!editor.mqtt.host.trim() && "填写 MQTT Broker 主机", editor.mqtt.port < 1 && "填写有效的 MQTT 端口", editor.mqtt.topics.length === 0 && "至少添加一个 MQTT 订阅主题", editor.mqtt.topics.some(item => !item.topic.trim()) && "补全 MQTT 订阅主题", editor.timestampMode === "source" && !editor.timestampPath.trim() && "填写时间戳字段路径");
    } else if (editor.protocol === "opc-ua") {
      issues.push(!/^opc\.tcp:\/\/|^https:\/\//i.test(editor.opcUa.endpointUrl) && "填写有效的 OPC UA 服务器端点", editor.opcUa.samplingIntervalMs < 100 && "OPC UA 采样周期不能小于 100 毫秒", editor.opcUa.securityMode !== "none" && !editor.opcUa.clientCertificatePath.trim() && "安全通道需要客户端证书路径", editor.opcUa.authenticationType === "username" && !editor.opcUa.username.trim() && "填写 OPC UA 用户名", editor.opcUa.authenticationType === "certificate" && !editor.opcUa.clientCertificatePath.trim() && "填写 OPC UA 客户端证书路径");
    } else {
      issues.push(!editor.modbusTcp.host.trim() && "填写 Modbus TCP 设备主机", editor.modbusTcp.port < 1 && "填写有效的 Modbus TCP 端口", editor.modbusTcp.pollIntervalMs < 100 && "Modbus TCP 采集周期不能小于 100 毫秒");
    }
    return issues.filter(Boolean);
  }
  if (step === 2) {
    const enabled = mappingRows.value.filter(row => row.enabled);
    const selectorMissing = row => editor.protocol === "modbus-tcp" ? row.modbusAddress === null || row.modbusAddress === undefined || !row.modbusArea : !row.sourcePath?.trim();
    const requiredMissing = mappingRows.value.some(row => !row.nullable && (!row.enabled || selectorMissing(row)));
    const contextInvalid = editor.contextMappings.some(row => !row.contextKey?.trim() || !row.sourcePath?.trim());
    const recipeInvalid = editor.recipe && (!editor.recipe.idPath?.trim() || !editor.recipe.versionPath?.trim() || (jsonPayloadProtocol.value && !editor.recipe.parametersPath?.trim()));
    const lifecycleInvalid = editor.lifecycle && (!editor.lifecycle.correlationIdContextKey?.trim() || !editor.contextMappings.some(row => row.contextKey === editor.lifecycle.correlationIdContextKey) || (editor.lifecycle.stepContextKey && !editor.contextMappings.some(row => row.contextKey === editor.lifecycle.stepContextKey)));
    return [
      enabled.length === 0 && "至少启用一个采集数据项",
      enabled.some(selectorMissing) && `补全已启用数据项的${sourceSelectorLabel.value}`,
      requiredMissing && "补全工艺数据模型中的必填数据项",
      contextInvalid && "补全运行上下文映射",
      recipeInvalid && "补全配方编号、版本和参数路径",
      lifecycleInvalid && "补全周期关联号和步序上下文映射",
    ].filter(Boolean);
  }
  return [];
}

function nextStep() {
  const issues = stepIssues(activeStep.value);
  if (editable.value && issues.length) {
    ElMessage.warning(issues[0]);
    return;
  }
  activeStep.value = Math.min(3, activeStep.value + 1);
}

async function save(status) {
  if (status === "published" && validationIssues.value.length) {
    ElMessage.warning(validationIssues.value[0]);
    return;
  }
  saving.value = true;
  try {
    updateValueMappings(); if (editor.recipe) updateRecipeMappings();
    await postJson("/api/v1/acquisition-profiles", { ...clonePlain(editor), status });
    ElMessage.success(status === "published" ? "采集任务已发布" : "采集任务草稿已保存");
    editorVisible.value = false; await load();
  } catch (cause) { ElMessage.error(cause.message); }
  finally { saving.value = false; }
}
async function removeProfile(row) {
  await ElMessageBox.confirm("删除该草稿后不可恢复。", "删除采集任务草稿", { type: "warning" });
  await deleteJson(`/api/v1/acquisition-profiles/${encodeURIComponent(row.profileId)}/${row.version}`);
  ElMessage.success("采集任务草稿已删除"); await load();
}
async function retireProfile(row) {
  await ElMessageBox.confirm("停用后，采集节点将停止运行此任务版本。", "停用采集任务", { type: "warning" });
  await postJson("/api/v1/acquisition-profiles", { ...clonePlain(row), status: "retired" });
  ElMessage.success("采集任务已停用"); await load();
}

onMounted(() => {
  load();
  runtimeTimer = window.setInterval(loadRuntimeStatuses, 15000);
});
onBeforeUnmount(() => window.clearInterval(runtimeTimer));
</script>

<style scoped>
.catalog-card { border-radius: 10px; }
.catalog-heading, .section-title, .drawer-actions { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
.catalog-heading > div, .drawer-heading { display: grid; gap: 4px; }
.catalog-heading strong { font-size: 16px; color: #172033; }
.catalog-heading span, .drawer-heading span, .section-title span, .secondary { color: #8a94a5; font-size: 12px; }
.secondary { margin-top: 4px; }
.list-toolbar { display: flex; gap: 12px; margin-bottom: 16px; }
.list-toolbar .el-input { width: min(360px, 100%); }
.list-toolbar .el-select { width: 150px; }
.task-steps { margin: 0 0 18px; }
.profile-form { display: grid; gap: 18px; }
.mapping-sections { display: grid; gap: 18px; }
.form-section { padding: 18px; border: 1px solid #e5e9ef; border-radius: 10px; background: #fff; }
.form-section h3 { margin: 0 0 16px; color: #1d293d; font-size: 15px; }
.section-title h3 { margin-bottom: 16px; }
.section-title h4 { margin: 0; color: #344054; font-size: 14px; }
.form-grid { display: grid; gap: 0 16px; }
.form-grid.two { grid-template-columns: repeat(2, minmax(0, 1fr)); }
.form-grid.three { grid-template-columns: repeat(3, minmax(0, 1fr)); }
.span-two { grid-column: span 2; }
.unit { margin-left: 8px; color: #7b8492; }
.inline-options { display: flex; align-items: center; gap: 20px; min-height: 32px; }
.topic-editor { margin-bottom: 18px; }
.topic-row { display: grid; grid-template-columns: minmax(0, 1fr) 110px 48px; gap: 10px; margin-top: 10px; }
.modbus-mapping { display: grid; grid-template-columns: minmax(112px, 1.3fr) 100px minmax(92px, 1fr); gap: 6px; }
.modbus-mapping > :nth-child(n + 4) { margin-top: 2px; }
.transform-row { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }
.drawer-actions { justify-content: flex-end; }
.review-section { display: grid; gap: 18px; }
.validation-panel { padding: 16px; border: 1px solid #f2cf8d; border-radius: 8px; background: #fffaf0; color: #6f4f16; }
.validation-panel ul { margin: 10px 0 0; padding-left: 20px; }
.validation-panel li + li { margin-top: 6px; }
.validation-ready { display: flex; align-items: center; gap: 12px; padding: 16px; border: 1px solid #b9dfc7; border-radius: 8px; background: #f4fbf6; color: #237744; }
.validation-ready .el-icon { font-size: 24px; }
.validation-ready div { display: grid; gap: 3px; }
.validation-ready span { color: #647568; font-size: 12px; }
:deep(.el-drawer__body) { background: #f6f8fb; }
:deep(.el-form-item__content > .el-select), :deep(.el-form-item__content > .el-input), :deep(.el-input-number) { width: 100%; }
@media (max-width: 760px) {
  .form-grid.two, .form-grid.three { grid-template-columns: 1fr; }
  .span-two { grid-column: auto; }
  .list-toolbar { align-items: stretch; flex-direction: column; }
  .list-toolbar .el-input, .list-toolbar .el-select { width: 100%; }
}
</style>
