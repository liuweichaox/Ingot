<template>
  <div class="setup-view">
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />

    <div v-if="isOperationSection" class="summary-grid">
      <article><span>已生效设备</span><strong>{{ activeContexts.length }}</strong><small>台设备已完成生产切换</small></article>
      <article><span>已装工装</span><strong>{{ activeInstallations.length }}</strong><small>当前设备安装状态</small></article>
      <article><span>工装</span><strong>{{ assemblies.length }}</strong><small>{{ revisions.length }} 个不可变组合版本</small></article>
      <article><span>组件</span><strong>{{ components.length }}</strong><small>可独立更换与复用</small></article>
    </div>

    <el-card v-loading="loading" shadow="never" class="setup-card">
      <el-tabs v-model="activeTab" class="section-tabs">
        <el-tab-pane label="生产切换" name="context">
          <div class="section-heading registry-heading">
            <div><strong>生产配置历史</strong><span>记录设备当前生效的产品、配方和已装工装</span></div>
            <el-button type="primary" :disabled="!activeInstallations.length" @click="openContextDrawer()">生产切换</el-button>
          </div>
          <el-alert
            v-if="!activeInstallations.length"
            title="当前没有已装工装，请先到“工装装卸”完成装入"
            type="info"
            :closable="false"
            show-icon
            class="section-alert"
          />
          <section class="table-panel">
            <el-table :data="pagedContexts" stripe>
              <el-table-column label="生产配置" min-width="300">
                <template #default="{ row }">
                  <div class="record-primary">{{ row.machineId }}</div>
                  <div class="record-secondary">{{ row.productSeries }} · {{ row.productCode }} / {{ row.recipeId }} · v{{ row.recipeVersion }}</div>
                </template>
              </el-table-column>
              <el-table-column label="生效记录" min-width="190">
                <template #default="{ row }">
                  <div>{{ formatTime(row.validFrom) }}</div>
                  <div class="record-tags">
                    <el-tag size="small" effect="plain">{{ sourceLabel(row.source) }}</el-tag>
                    <el-tag size="small" :type="row.validTo ? 'info' : 'success'">{{ row.validTo ? '已结束' : '生效中' }}</el-tag>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="80" fixed="right">
                <template #default="{ row }">
                  <el-button v-if="!row.validTo" link @click="closeContext(row)">结束</el-button>
                </template>
              </el-table-column>
            </el-table>
            <el-empty v-if="!contexts.length" description="尚无生产配置记录" />
            <div v-if="contexts.length > contextPageSize" class="table-pagination">
              <el-pagination
                v-model:current-page="contextPage"
                :page-size="contextPageSize"
                :total="contexts.length"
                layout="total, prev, pager, next"
              />
            </div>
          </section>
        </el-tab-pane>

        <el-tab-pane label="装卸记录" name="installation">
          <div class="section-heading registry-heading">
            <div><strong>当前安装</strong><span>每台设备同一时间只允许一套工装处于已装状态</span></div>
            <el-button type="primary" @click="openInstallationDrawer">装入工装</el-button>
          </div>
          <section class="table-panel">
            <el-table :data="activeInstallations" stripe>
              <el-table-column label="设备与工装" min-width="300">
                <template #default="{ row }">
                  <div class="record-primary">{{ row.machineId }}</div>
                  <div class="record-secondary">{{ revisionLabel(revisionById(row.assemblyRevisionId)) }}</div>
                </template>
              </el-table-column>
              <el-table-column label="装入信息" min-width="210">
                <template #default="{ row }">
                  <div>{{ formatTime(row.installedAt) }}</div>
                  <div class="record-secondary">{{ sourceLabel(row.source) }}{{ row.actor ? ` · ${row.actor}` : "" }}</div>
                </template>
              </el-table-column>
              <el-table-column label="生产状态" min-width="220">
                <template #default="{ row }">
                  <template v-if="currentContextFor(row)">
                    <div class="record-primary">{{ currentContextFor(row).productSeries }} · {{ currentContextFor(row).productCode }}</div>
                    <div class="record-secondary">{{ currentContextFor(row).recipeId }} · v{{ currentContextFor(row).recipeVersion }}</div>
                  </template>
                  <el-tag v-else type="warning" effect="plain">待生产切换</el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="170" fixed="right">
                <template #default="{ row }">
                  <el-button link type="primary" @click="openContextDrawer(row)">生产切换</el-button>
                  <el-button link type="danger" @click="removeTooling(row)">卸下</el-button>
                </template>
              </el-table-column>
            </el-table>
            <el-empty v-if="!activeInstallations.length" description="当前没有设备安装工装" />
          </section>

          <div class="section-heading history-heading">
            <div><strong>装卸历史</strong><span>现场事实只结束有效区间，不从历史中删除</span></div>
          </div>
          <section class="table-panel">
            <el-table :data="pagedInstallationHistory" stripe>
              <el-table-column label="设备与工装" min-width="280">
                <template #default="{ row }">
                  <div class="record-primary">{{ row.machineId }}</div>
                  <div class="record-secondary">{{ revisionLabel(revisionById(row.assemblyRevisionId)) }}</div>
                </template>
              </el-table-column>
              <el-table-column label="使用区间" min-width="280">
                <template #default="{ row }">
                  <div>{{ formatTime(row.installedAt) }} — {{ formatTime(row.removedAt) }}</div>
                  <div class="record-secondary">使用 {{ durationLabel(row.installedAt, row.removedAt) }}</div>
                </template>
              </el-table-column>
              <el-table-column label="来源与操作人" min-width="170">
                <template #default="{ row }">
                  <div>{{ sourceLabel(row.source) }}</div>
                  <div class="record-secondary">{{ row.actor || "未记录操作人" }}</div>
                </template>
              </el-table-column>
            </el-table>
            <el-empty v-if="!installationHistory.length" description="尚无已结束的工装装卸记录" />
            <div v-if="installationHistory.length > installationPageSize" class="table-pagination">
              <el-pagination
                v-model:current-page="installationPage"
                :page-size="installationPageSize"
                :total="installationHistory.length"
                layout="total, prev, pager, next"
              />
            </div>
          </section>
        </el-tab-pane>

        <el-tab-pane label="模具组合" name="assembly">
          <div class="section-heading registry-heading">
            <div><strong>工装组合</strong><span>稳定编号与不可变组件组合版本</span></div>
            <el-button type="primary" @click="newAssembly">新建工装</el-button>
          </div>
          <el-table :data="pagedAssemblies" stripe class="history-table">
            <el-table-column prop="moldId" label="模具编号" width="150" />
            <el-table-column prop="name" label="名称" min-width="180" />
            <el-table-column label="工装类型" min-width="180"><template #default="{ row }">{{ typeByCode(row.toolingTypeCode)?.name || row.toolingTypeCode }}</template></el-table-column>
            <el-table-column label="状态" width="90"><template #default="{ row }">{{ statusLabel(row.status) }}</template></el-table-column>
            <el-table-column label="组合版本" width="100"><template #default="{ row }">{{ revisions.filter(item => item.moldId === row.moldId).length }}</template></el-table-column>
            <el-table-column label="操作" width="210"><template #default="{ row }"><el-button link type="primary" @click="editAssembly(row)">编辑</el-button><el-button link type="primary" @click="newRevision(row)">新组合版本</el-button><el-button link type="danger" @click="deleteAssembly(row)">删除</el-button></template></el-table-column>
          </el-table>
          <TablePagination v-model:page="assemblyPage" v-model:page-size="assemblyPageSize" :total="assemblyTotal" />
          <el-table :data="pagedRevisions" stripe class="history-table">
            <el-table-column prop="moldId" label="模具编号" width="150" />
            <el-table-column prop="revision" label="版本" width="90" />
            <el-table-column label="组件成员" min-width="480">
              <template #default="{ row }">
                <el-tag v-for="member in row.members" :key="member.roleCode" class="member-tag" effect="plain">{{ roleName(row.moldId, member.roleCode) }}：{{ member.componentId }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="创建时间" width="175"><template #default="{ row }">{{ formatTime(row.createdAt) }}</template></el-table-column>
            <el-table-column label="操作" width="80"><template #default="{ row }"><el-button link type="danger" @click="deleteRevision(row)">删除</el-button></template></el-table-column>
          </el-table>
          <TablePagination v-model:page="revisionPage" v-model:page-size="revisionPageSize" :total="revisionTotal" />
        </el-tab-pane>

        <el-tab-pane label="组件类型" name="componentType">
          <div class="section-heading registry-heading">
            <div><strong>组件类型</strong><span>组件台账使用的分类字典</span></div>
            <el-button type="primary" @click="newComponentType">新建类型</el-button>
          </div>
          <section class="table-panel">
            <el-table :data="pagedComponentTypes" stripe>
              <el-table-column prop="componentTypeCode" label="类型代码" min-width="180" />
              <el-table-column prop="name" label="显示名称" min-width="160" />
              <el-table-column label="状态" width="110"><template #default="{ row }">{{ statusLabel(row.status) }}</template></el-table-column>
              <el-table-column label="组件数量" width="110"><template #default="{ row }">{{ componentTypeCount(row.componentTypeCode) }}</template></el-table-column>
              <el-table-column label="操作" width="130"><template #default="{ row }"><el-button link type="primary" @click="editComponentType(row)">编辑</el-button><el-button link type="danger" @click="deleteComponentType(row)">删除</el-button></template></el-table-column>
            </el-table>
            <el-empty v-if="!componentTypes.length" description="请先配置组件类型，再登记组件" />
            <TablePagination v-model:page="componentTypePage" v-model:page-size="componentTypePageSize" :total="componentTypeTotal" />
          </section>
        </el-tab-pane>

        <el-tab-pane label="组件台账" name="component">
          <div class="section-heading registry-heading">
            <div><strong>组件台账</strong><span>可独立更换、复用和追溯的物理组件</span></div>
            <el-button type="primary" @click="newComponent">登记组件</el-button>
          </div>
          <el-table :data="pagedComponents" stripe>
            <el-table-column prop="componentId" label="组件 ID" min-width="150" />
            <el-table-column prop="serialNo" label="序列号" min-width="150" />
            <el-table-column prop="name" label="名称" min-width="160" />
            <el-table-column label="组件类型" min-width="160"><template #default="{ row }">{{ componentTypeLabel(row) }}</template></el-table-column>
            <el-table-column label="当前装配角色" min-width="220"><template #default="{ row }">{{ currentRoleLabels(row.componentId) }}</template></el-table-column>
            <el-table-column label="状态" width="100"><template #default="{ row }">{{ statusLabel(row.status) }}</template></el-table-column>
            <el-table-column label="操作" width="140" fixed="right"><template #default="{ row }"><el-button link type="primary" @click="editComponent(row)">编辑</el-button><el-button link type="danger" @click="deleteComponent(row)">删除</el-button></template></el-table-column>
          </el-table>
          <el-empty v-if="!components.length" description="暂无组件，请先登记组件" />
          <TablePagination v-model:page="componentPage" v-model:page-size="componentPageSize" :total="componentTotal" />
        </el-tab-pane>

        <el-tab-pane label="工装类型" name="type">
          <div class="section-heading registry-heading">
            <div><strong>工装类型</strong><span>定义装配位置及允许的组件类型</span></div>
            <el-button type="primary" @click="newToolingType">新建类型</el-button>
          </div>
          <section class="table-panel">
            <el-table :data="pagedToolingTypes" stripe>
              <el-table-column prop="toolingTypeCode" label="类型代码" min-width="170" />
              <el-table-column prop="version" label="版本" width="80" />
              <el-table-column prop="name" label="名称" min-width="150" />
              <el-table-column label="装配位置" min-width="260"><template #default="{ row }">{{ row.roles.map(role => role.name).join('、') }}</template></el-table-column>
              <el-table-column label="状态" width="90"><template #default="{ row }">{{ statusLabel(row.status) }}</template></el-table-column>
              <el-table-column label="操作" width="200"><template #default="{ row }"><el-button link type="primary" @click="baseToolingTypeOn(row)">新版本维护</el-button><el-button link type="danger" @click="deleteToolingType(row)">删除版本</el-button></template></el-table-column>
            </el-table>
            <TablePagination v-model:page="toolingTypePage" v-model:page-size="toolingTypePageSize" :total="toolingTypeTotal" />
          </section>
        </el-tab-pane>
      </el-tabs>
    </el-card>

    <el-drawer v-model="contextDrawerVisible" title="生产切换" size="620px" destroy-on-close>
      <el-form label-position="top" class="drawer-form">
        <div class="form-grid">
          <el-form-item label="设备">
            <el-select v-model="contextForm.machineId" filterable @change="syncContextInstallation">
              <el-option v-for="machineId in activeMachineIds" :key="machineId" :label="machineId" :value="machineId" />
            </el-select>
          </el-form-item>
          <el-form-item label="当前工装">
            <el-input :model-value="selectedContextInstallation ? revisionLabel(revisionById(selectedContextInstallation.assemblyRevisionId)) : ''" disabled />
          </el-form-item>
        </div>
        <div class="form-grid">
          <el-form-item label="产品系列"><el-input v-model="contextForm.productSeries" /></el-form-item>
          <el-form-item label="产品型号"><el-input v-model="contextForm.productCode" /></el-form-item>
          <el-form-item label="配方版本" class="span-two">
            <el-select :model-value="selectedRecipeKey" filterable placeholder="选择已发布配方" @change="selectRecipe">
              <el-option v-for="item in publishedRecipes" :key="recipeKey(item)" :label="`${item.name} · ${item.recipeId} · v${item.version}`" :value="recipeKey(item)" />
            </el-select>
          </el-form-item>
          <el-form-item label="物料批号（可选）"><el-input v-model="contextForm.materialLotRef" /></el-form-item>
        </div>
      </el-form>
      <template #footer><div class="drawer-actions"><el-button @click="contextDrawerVisible = false">取消</el-button><el-button type="primary" :loading="saving" @click="startContext">确认切换</el-button></div></template>
    </el-drawer>

    <el-drawer v-model="installationDrawerVisible" title="装入工装" size="520px" destroy-on-close>
      <el-form label-position="top" class="drawer-form">
        <el-form-item label="设备">
          <el-select v-model="installationForm.machineId" filterable allow-create default-first-option placeholder="选择或输入设备 ID">
            <el-option v-for="machineId in knownMachineIds" :key="machineId" :label="machineId" :value="machineId" />
          </el-select>
        </el-form-item>
        <el-form-item label="工装组合版本">
          <el-select v-model="installationForm.assemblyRevisionId" filterable>
            <el-option v-for="item in installableRevisions" :key="item.assemblyRevisionId" :label="revisionLabel(item)" :value="item.assemblyRevisionId" />
          </el-select>
        </el-form-item>
        <el-alert
          v-if="!installableRevisions.length"
          title="当前没有可装入的工装组合版本，请检查工装、组件状态或现有安装"
          type="warning"
          :closable="false"
          show-icon
        />
      </el-form>
      <template #footer><div class="drawer-actions"><el-button @click="installationDrawerVisible = false">取消</el-button><el-button type="primary" :loading="saving" :disabled="!installationForm.machineId || !installationForm.assemblyRevisionId" @click="installTooling">确认装入</el-button></div></template>
    </el-drawer>

    <el-drawer
      v-model="editorVisible"
      :title="maintenanceDrawerTitle"
      :size="activeTab === 'type' ? '760px' : '540px'"
      destroy-on-close
    >
      <el-form v-if="activeTab === 'componentType'" label-position="top" class="drawer-form">
        <el-form-item label="类型代码"><el-input v-model="componentTypeForm.componentTypeCode" :disabled="Boolean(editingComponentTypeCode)" placeholder="例如 mold_core" /></el-form-item>
        <el-form-item label="显示名称"><el-input v-model="componentTypeForm.name" placeholder="例如 模芯" /></el-form-item>
        <el-form-item label="状态"><el-select v-model="componentTypeForm.status"><el-option label="启用" value="active" /><el-option label="停用" value="inactive" /></el-select></el-form-item>
      </el-form>

      <el-form v-else-if="activeTab === 'assembly' && assemblyEditorMode === 'identity'" label-position="top" class="drawer-form">
        <el-form-item label="模具编号"><el-input v-model="assemblyForm.moldId" :disabled="Boolean(editingAssemblyId)" /></el-form-item>
        <el-form-item label="名称"><el-input v-model="assemblyForm.name" /></el-form-item>
        <el-form-item label="工装类型"><el-select v-model="assemblyForm.toolingTypeCode"><el-option v-for="item in latestTypes" :key="item.toolingTypeCode" :label="item.name" :value="item.toolingTypeCode" /></el-select></el-form-item>
        <el-form-item label="状态"><el-select v-model="assemblyForm.status"><el-option label="启用" value="active" /><el-option label="停用" value="inactive" /></el-select></el-form-item>
      </el-form>

      <el-form v-else-if="activeTab === 'assembly'" label-position="top" class="drawer-form">
        <el-form-item label="模具"><el-select v-model="revisionForm.moldId" @change="resetRevisionMembers"><el-option v-for="item in assemblies" :key="item.moldId" :label="`${item.moldId} · ${item.name}`" :value="item.moldId" /></el-select></el-form-item>
        <el-form-item label="版本号"><el-input-number v-model="revisionForm.revision" :min="1" /></el-form-item>
        <el-form-item v-for="role in revisionRoles" :key="role.code" :label="role.name">
          <el-select v-model="revisionForm.members[role.code]" filterable><el-option v-for="item in componentsForRole(role)" :key="item.componentId" :label="`${item.componentId} · ${item.serialNo} · ${componentTypeLabel(item)}`" :value="item.componentId" /></el-select>
        </el-form-item>
      </el-form>

      <el-form v-else-if="activeTab === 'type'" label-position="top" class="drawer-form">
        <div class="form-grid">
          <el-form-item label="类型代码"><el-input v-model="typeForm.toolingTypeCode" /></el-form-item>
          <el-form-item label="名称"><el-input v-model="typeForm.name" /></el-form-item>
          <el-form-item label="版本"><el-input-number v-model="typeForm.version" :min="1" /></el-form-item>
          <el-form-item label="状态"><el-select v-model="typeForm.status"><el-option label="启用" value="active" /><el-option label="停用" value="inactive" /></el-select></el-form-item>
        </div>
        <el-table :data="typeForm.roles" size="small">
          <el-table-column label="位置代码" min-width="130"><template #default="{ row }"><el-input v-model="row.code" /></template></el-table-column>
          <el-table-column label="显示名称" min-width="130"><template #default="{ row }"><el-input v-model="row.name" /></template></el-table-column>
          <el-table-column label="允许的组件类型（空=不限）" min-width="220"><template #default="{ row }"><el-select v-model="row.acceptedComponentTypeCodes" multiple filterable><el-option v-for="item in componentTypeOptions" :key="item.code" :label="`${item.name}（${item.code}）`" :value="item.code" /></el-select></template></el-table-column>
          <el-table-column label="必需" width="70"><template #default="{ row }"><el-switch v-model="row.required" /></template></el-table-column>
          <el-table-column width="70"><template #default="{ $index }"><el-button link type="danger" @click="typeForm.roles.splice($index, 1)">删除</el-button></template></el-table-column>
        </el-table>
        <el-button class="drawer-secondary-action" @click="typeForm.roles.push({ code: '', name: '', required: true, maxCount: 1, sortOrder: typeForm.roles.length + 1, acceptedComponentTypeCodes: [] })">新增装配位置</el-button>
      </el-form>

      <template #footer>
        <div class="drawer-actions">
          <el-button @click="editorVisible = false">取消</el-button>
          <el-button v-if="activeTab === 'componentType'" type="primary" :loading="saving" @click="saveComponentType">{{ editingComponentTypeCode ? '保存修改' : '保存组件类型' }}</el-button>
          <el-button v-else-if="activeTab === 'assembly' && assemblyEditorMode === 'identity'" type="primary" :loading="saving" @click="saveAssembly">{{ editingAssemblyId ? '保存修改' : '保存工装' }}</el-button>
          <el-button v-else-if="activeTab === 'assembly'" type="primary" :loading="saving" @click="createRevision">创建组合版本</el-button>
          <el-button v-else-if="activeTab === 'type'" type="primary" :loading="saving" @click="createType">发布新版本</el-button>
        </div>
      </template>
    </el-drawer>

    <el-drawer
      v-model="componentDrawerVisible"
      :title="editingComponentId ? '编辑组件' : '登记组件'"
      size="520px"
      destroy-on-close
    >
      <el-form label-position="top" class="drawer-form">
        <el-form-item label="组件 ID"><el-input v-model="componentForm.componentId" :disabled="Boolean(editingComponentId)" /></el-form-item>
        <el-form-item label="序列号"><el-input v-model="componentForm.serialNo" /></el-form-item>
        <el-form-item label="名称"><el-input v-model="componentForm.name" /></el-form-item>
        <el-form-item label="组件类型">
          <el-select v-model="componentForm.componentTypeCode" filterable placeholder="选择已配置的组件类型">
            <el-option v-for="item in componentTypeOptions" :key="item.code" :label="`${item.name}（${item.code}）`" :value="item.code" />
          </el-select>
        </el-form-item>
        <el-form-item label="状态"><el-select v-model="componentForm.status"><el-option label="可用" value="available" /><el-option label="维护中" value="maintenance" /><el-option label="已退役" value="retired" /></el-select></el-form-item>
      </el-form>
      <template #footer>
        <div class="drawer-actions">
          <el-button @click="componentDrawerVisible = false">取消</el-button>
          <el-button type="primary" :loading="saving" @click="saveComponent">{{ editingComponentId ? '保存修改' : '保存组件' }}</el-button>
        </div>
      </template>
    </el-drawer>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import { deleteJson, getJson, postJson } from "../api/http";
import TablePagination from "../components/TablePagination.vue";
import { useClientPagination } from "../composables/useClientPagination";

const props = defineProps({ section: { type: String, default: "context" } });
const activeTab = ref(props.section);
const loading = ref(false);
const saving = ref(false);
const editorVisible = ref(false);
const componentDrawerVisible = ref(false);
const contextDrawerVisible = ref(false);
const installationDrawerVisible = ref(false);
const assemblyEditorMode = ref("identity");
const error = ref("");
const componentTypes = ref([]);
const toolingTypes = ref([]);
const components = ref([]);
const assemblies = ref([]);
const revisions = ref([]);
const installations = ref([]);
const contexts = ref([]);
const recipes = ref([]);
const { page: componentTypePage, pageSize: componentTypePageSize, total: componentTypeTotal, pagedItems: pagedComponentTypes } = useClientPagination(componentTypes);
const { page: componentPage, pageSize: componentPageSize, total: componentTotal, pagedItems: pagedComponents } = useClientPagination(components);
const { page: toolingTypePage, pageSize: toolingTypePageSize, total: toolingTypeTotal, pagedItems: pagedToolingTypes } = useClientPagination(toolingTypes);
const { page: assemblyPage, pageSize: assemblyPageSize, total: assemblyTotal, pagedItems: pagedAssemblies } = useClientPagination(assemblies);
const { page: revisionPage, pageSize: revisionPageSize, total: revisionTotal, pagedItems: pagedRevisions } = useClientPagination(revisions);
const contextPage = ref(1);
const contextPageSize = 20;
const installationPage = ref(1);
const installationPageSize = 20;

const componentTypeForm = reactive({ componentTypeCode: "", name: "", status: "active", attributes: {} });
const typeForm = reactive({ toolingTypeCode: "", version: 1, name: "", status: "active", roles: [] });
const componentForm = reactive({ componentId: "", componentTypeCode: "", serialNo: "", name: "", status: "available", attributes: {} });
const assemblyForm = reactive({ moldId: "", toolingTypeCode: "", name: "", status: "active" });
const revisionForm = reactive({ moldId: "", revision: 1, members: {} });
const installationForm = reactive({ machineId: "", assemblyRevisionId: "", source: "manual" });
const contextForm = reactive({ machineId: "", productSeries: "", productCode: "", recipeId: "", recipeVersion: "", toolingInstallationId: "", source: "manual", materialLotRef: "" });
const editingComponentTypeCode = ref("");
const editingComponentId = ref("");
const editingAssemblyId = ref("");

const activeInstallations = computed(() => installations.value.filter(item => !item.removedAt));
const installationHistory = computed(() => installations.value.filter(item => item.removedAt));
const activeContexts = computed(() => contexts.value.filter(item => !item.validTo));
const activeMachineIds = computed(() => [...new Set(activeInstallations.value.map(item => item.machineId))].sort());
const knownMachineIds = computed(() => [...new Set([
  ...installations.value.map(item => item.machineId),
  ...contexts.value.map(item => item.machineId),
])].sort());
const selectedContextInstallation = computed(() => activeInstallations.value.find(
  item => item.installationId === contextForm.toolingInstallationId
));
const occupiedComponentIds = computed(() => new Set(activeInstallations.value.flatMap(installation => {
  const revision = revisions.value.find(item => item.assemblyRevisionId === installation.assemblyRevisionId);
  return revision?.members?.map(member => member.componentId) || [];
})));
const installableRevisions = computed(() => revisions.value.filter(revision => {
  const assembly = assemblies.value.find(item => item.moldId === revision.moldId);
  if (!assembly || assembly.status !== "active") return false;
  return (revision.members || []).every(member => {
    const component = components.value.find(item => item.componentId === member.componentId);
    return component?.status === "available" && !occupiedComponentIds.value.has(member.componentId);
  });
}));
const publishedRecipes = computed(() => recipes.value.filter(item => item.status === "published"));
const selectedRecipeKey = computed(() => contextForm.recipeId ? `${contextForm.recipeId}@${contextForm.recipeVersion}` : "");
const pagedContexts = computed(() => contexts.value.slice(
  (contextPage.value - 1) * contextPageSize,
  contextPage.value * contextPageSize,
));
const pagedInstallationHistory = computed(() => installationHistory.value.slice(
  (installationPage.value - 1) * installationPageSize,
  installationPage.value * installationPageSize,
));
const isOperationSection = computed(() => ["context", "installation"].includes(activeTab.value));
const latestTypes = computed(() => {
  const result = new Map();
  for (const item of toolingTypes.value) if (!result.has(item.toolingTypeCode)) result.set(item.toolingTypeCode, item);
  return [...result.values()];
});
const componentTypeOptions = computed(() => componentTypes.value
  .filter(item => item.status === "active")
  .map(item => ({ code: item.componentTypeCode, name: item.name })));
const revisionAssembly = computed(() => assemblies.value.find(item => item.moldId === revisionForm.moldId));
const revisionRoles = computed(() => typeByCode(revisionAssembly.value?.toolingTypeCode)?.roles || []);
const maintenanceDrawerTitle = computed(() => {
  if (activeTab.value === "componentType") return editingComponentTypeCode.value ? "编辑组件类型" : "新建组件类型";
  if (activeTab.value === "assembly") return assemblyEditorMode.value === "revision" ? "创建组合版本" : (editingAssemblyId.value ? "编辑工装" : "新建工装");
  if (activeTab.value === "type") return typeForm.toolingTypeCode ? "维护工装类型版本" : "新建工装类型";
  return "维护记录";
});

async function loadAll() {
  loading.value = true;
  error.value = "";
  try {
    const [componentTypeRows, types, componentRows, assemblyRows, revisionRows, installationRows, contextRows, recipeRows] = await Promise.all([
      getJson("/api/v1/tooling-component-types"), getJson("/api/v1/tooling-types"), getJson("/api/v1/tooling-components"), getJson("/api/v1/tooling-assemblies"),
      getJson("/api/v1/tooling-assemblies/revisions"),
      getJson("/api/v1/tooling-installations"), getJson("/api/v1/production-contexts"), getJson("/api/v1/recipe-versions"),
    ]);
    componentTypes.value = componentTypeRows.data || [];
    toolingTypes.value = types.data || [];
    components.value = componentRows.data || [];
    assemblies.value = assemblyRows.data || [];
    revisions.value = revisionRows.data || [];
    installations.value = installationRows.data || [];
    contexts.value = contextRows.data || [];
    recipes.value = recipeRows.data || [];
    contextPage.value = Math.min(contextPage.value, Math.max(1, Math.ceil(contexts.value.length / contextPageSize)));
    installationPage.value = Math.min(installationPage.value, Math.max(1, Math.ceil(installationHistory.value.length / installationPageSize)));
  } catch (requestError) { error.value = requestError.message; }
  finally { loading.value = false; }
}

async function runSave(action, message) {
  saving.value = true; error.value = "";
  try { await action(); ElMessage.success(message); await loadAll(); editorVisible.value = false; }
  catch (requestError) { error.value = requestError.message; }
  finally { saving.value = false; }
}

function openContextDrawer(installation = null) {
  Object.assign(contextForm, { machineId: "", productSeries: "", productCode: "", recipeId: "", recipeVersion: "", toolingInstallationId: "", source: "manual", materialLotRef: "" });
  if (installation) {
    contextForm.machineId = installation.machineId;
    contextForm.toolingInstallationId = installation.installationId;
  }
  contextDrawerVisible.value = true;
}
function openInstallationDrawer() {
  Object.assign(installationForm, { machineId: "", assemblyRevisionId: "", source: "manual" });
  installationDrawerVisible.value = true;
}

function newComponentType() { Object.assign(componentTypeForm, { componentTypeCode: "", name: "", status: "active", attributes: {} }); editingComponentTypeCode.value = ""; editorVisible.value = true; }
function editComponentType(row) { Object.assign(componentTypeForm, JSON.parse(JSON.stringify(row))); editingComponentTypeCode.value = row.componentTypeCode; editorVisible.value = true; }
function newComponent() { Object.assign(componentForm, { componentId: "", componentTypeCode: "", serialNo: "", name: "", status: "available", attributes: {} }); editingComponentId.value = ""; componentDrawerVisible.value = true; }
function editComponent(row) { Object.assign(componentForm, JSON.parse(JSON.stringify(row))); editingComponentId.value = row.componentId; componentDrawerVisible.value = true; }
function newAssembly() { Object.assign(assemblyForm, { moldId: "", toolingTypeCode: "", name: "", status: "active" }); editingAssemblyId.value = ""; assemblyEditorMode.value = "identity"; editorVisible.value = true; }
function editAssembly(row) { Object.assign(assemblyForm, JSON.parse(JSON.stringify(row))); editingAssemblyId.value = row.moldId; assemblyEditorMode.value = "identity"; editorVisible.value = true; }
function newRevision(row) { revisionForm.moldId = row.moldId; resetRevisionMembers(); assemblyEditorMode.value = "revision"; editorVisible.value = true; }
function newToolingType() { Object.assign(typeForm, { toolingTypeCode: "", version: 1, name: "", status: "active", roles: [] }); editorVisible.value = true; }
function baseToolingTypeOn(row) {
  const versions = toolingTypes.value.filter(item => item.toolingTypeCode === row.toolingTypeCode).map(item => item.version);
  Object.assign(typeForm, JSON.parse(JSON.stringify(row)), { version: Math.max(...versions, row.version) + 1, status: "active" });
  editorVisible.value = true;
}
function createType() { return runSave(() => postJson("/api/v1/tooling-types", typeForm), "工装类型版本已发布"); }
function saveComponentType() { return runSave(() => postJson("/api/v1/tooling-component-types", componentTypeForm), "组件类型已保存"); }
async function saveComponent() {
  saving.value = true; error.value = "";
  try { await postJson("/api/v1/tooling-components", componentForm); ElMessage.success("组件已保存"); await loadAll(); componentDrawerVisible.value = false; }
  catch (requestError) { error.value = requestError.message; }
  finally { saving.value = false; }
}
function saveAssembly() { return runSave(() => postJson("/api/v1/tooling-assemblies", assemblyForm), "模具已保存"); }
function createRevision() {
  const payload = { moldId: revisionForm.moldId, revision: revisionForm.revision, members: revisionRoles.value.map(role => ({ roleCode: role.code, componentId: revisionForm.members[role.code] })).filter(item => item.componentId) };
  return runSave(() => postJson(`/api/v1/tooling-assemblies/${encodeURIComponent(revisionForm.moldId)}/revisions`, payload), "不可变组合版本已创建");
}
async function installTooling() {
  await runSave(() => postJson("/api/v1/tooling-installations", { ...installationForm, installedAt: new Date().toISOString(), commandId: crypto.randomUUID() }), "工装已装入");
  if (!error.value) installationDrawerVisible.value = false;
}
async function startContext() {
  await runSave(() => postJson("/api/v1/production-contexts", { ...contextForm, validFrom: new Date().toISOString() }), "生产上下文已启用");
  if (!error.value) contextDrawerVisible.value = false;
}
async function removeTooling(row) {
  const activeContext = currentContextFor(row);
  const consequence = activeContext
    ? `同时结束 ${activeContext.productSeries} · ${activeContext.productCode} 的当前生产配置。`
    : "该设备当前没有生效中的生产配置。";
  await ElMessageBox.confirm(
    `将卸下 ${row.machineId} 上的 ${revisionLabel(revisionById(row.assemblyRevisionId))}，${consequence}历史生产记录仍保留原引用。`,
    "确认卸下工装",
    { type: "warning" }
  );
  return runSave(() => postJson(`/api/v1/tooling-installations/${row.installationId}:remove`, { at: new Date().toISOString() }), "工装已卸下");
}
async function closeContext(row) {
  await ElMessageBox.confirm("结束后，下一周期必须先启用新的生产上下文。", "结束生产上下文", { type: "warning" });
  return runSave(() => postJson(`/api/v1/production-contexts/${row.contextId}:close`, { at: new Date().toISOString() }), "生产上下文已结束");
}

async function runDelete(url, title, message) {
  await ElMessageBox.confirm("只能删除尚未形成历史引用的数据；已投入生产的数据请使用停用、退役或结束操作。", title, { type: "warning" });
  return runSave(() => deleteJson(url), message);
}
function deleteComponentType(row) { return runDelete(`/api/v1/tooling-component-types/${encodeURIComponent(row.componentTypeCode)}`, "删除组件类型", "组件类型已删除"); }
function deleteComponent(row) { return runDelete(`/api/v1/tooling-components/${encodeURIComponent(row.componentId)}`, "删除组件", "组件已删除"); }
function deleteAssembly(row) { return runDelete(`/api/v1/tooling-assemblies/${encodeURIComponent(row.moldId)}`, "删除工装", "工装已删除"); }
function deleteRevision(row) { return runDelete(`/api/v1/tooling-assemblies/revisions/${row.assemblyRevisionId}`, "删除组合版本", "组合版本已删除"); }
function deleteToolingType(row) { return runDelete(`/api/v1/tooling-types/${encodeURIComponent(row.toolingTypeCode)}/${row.version}`, "删除工装类型版本", "工装类型版本已删除"); }

function typeByCode(code) { return latestTypes.value.find(item => item.toolingTypeCode === code); }
function componentsForRole(role) {
  const accepted = role.acceptedComponentTypeCodes || [];
  return accepted.length ? components.value.filter(item => accepted.includes(item.componentTypeCode)) : components.value;
}
function revisionById(id) { return revisions.value.find(item => item.assemblyRevisionId === id); }
function revisionLabel(item) { return item ? `${item.moldId} · v${item.revision}` : "未知组合版本"; }
function roleName(moldId, code) { const assembly = assemblies.value.find(item => item.moldId === moldId); return typeByCode(assembly?.toolingTypeCode)?.roles.find(role => role.code === code)?.name || code; }
function componentTypeLabel(component) { return componentTypes.value.find(item => item.componentTypeCode === component.componentTypeCode)?.name || component.attributes?.componentTypeName || component.componentTypeCode; }
function componentTypeCount(code) { return components.value.filter(item => item.componentTypeCode === code).length; }
function currentRoleLabels(componentId) {
  const labels = revisions.value.flatMap(revision => revision.members
    .filter(member => member.componentId === componentId)
    .map(member => `${revision.moldId} / ${roleName(revision.moldId, member.roleCode)}`));
  return labels.length ? [...new Set(labels)].join("、") : "未装配";
}
function resetRevisionMembers() { revisionForm.members = {}; const existing = revisions.value.filter(item => item.moldId === revisionForm.moldId); revisionForm.revision = existing.length ? Math.max(...existing.map(item => item.revision)) + 1 : 1; }
function syncContextInstallation(machineId) {
  contextForm.toolingInstallationId = activeInstallations.value.find(item => item.machineId === machineId)?.installationId || "";
}
function currentContextFor(installation) {
  return activeContexts.value.find(item => item.toolingInstallationId === installation.installationId);
}
function recipeKey(item) { return `${item.recipeId}@${item.version}`; }
function selectRecipe(key) {
  const item = recipes.value.find(recipe => recipeKey(recipe) === key);
  if (!item) return;
  contextForm.recipeId = item.recipeId;
  contextForm.recipeVersion = String(item.version);
  contextForm.productSeries = item.contextSelector?.product_series || contextForm.productSeries;
  contextForm.productCode = item.contextSelector?.product_code || contextForm.productCode;
}
function sourceLabel(value) { return { manual: "现场", mes: "MES", device: "设备", import: "导入" }[value] || value; }
function statusLabel(value) { return { active: "启用", inactive: "停用", available: "可用", maintenance: "维护中", retired: "已退役" }[value] || value; }
function formatTime(value) { return value ? new Date(value).toLocaleString("zh-CN") : "-"; }
function durationLabel(start, end) {
  const seconds = Math.max(0, Math.round((new Date(end).getTime() - new Date(start).getTime()) / 1000));
  if (seconds < 3600) return `${Math.max(1, Math.round(seconds / 60))} 分钟`;
  const hours = seconds / 3600;
  return hours < 24 ? `${hours.toFixed(hours < 10 ? 1 : 0)} 小时` : `${(hours / 24).toFixed(1)} 天`;
}

watch(() => props.section, value => {
  activeTab.value = value;
  editorVisible.value = false;
  componentDrawerVisible.value = false;
  contextDrawerVisible.value = false;
  installationDrawerVisible.value = false;
});
onMounted(loadAll);
</script>

<style scoped>
.setup-view { display: grid; gap: 18px; }
.summary-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); overflow: hidden; border: 1px solid #e7ebf0; border-radius: 12px; background: #fff; }
.summary-grid article { display: grid; gap: 4px; padding: 14px 20px; border-right: 1px solid #edf0f4; }
.summary-grid article:last-child { border-right: 0; }
.summary-grid span, .summary-grid small, .card-heading span, .section-heading span, .form-panel p { color: #8b95a5; }
.summary-grid strong { color: #172033; font-size: 27px; }
.card-heading, .card-heading > div, .section-heading, .form-actions { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.card-heading > div { display: grid; justify-items: start; }
.card-heading span, .section-heading span { font-size: 12px; }
.split-layout { display: grid; grid-template-columns: minmax(320px, .72fr) minmax(560px, 1.7fr); gap: 22px; }
.split-layout.editor-closed { grid-template-columns: minmax(0, 1fr); }
.double-forms { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 22px; }
.assembly-editor { max-width: 720px; }
.registry-heading { margin-bottom: 16px; }
.registry-heading > div { display: grid; gap: 4px; }
.history-heading { margin: 28px 0 14px; }
.history-heading > div { display: grid; gap: 4px; }
.section-alert { margin-bottom: 16px; }
.editor-region { margin-bottom: 18px; }
.form-panel { padding: 18px; border: 1px solid #e8ecf1; border-radius: 10px; background: #fbfcfe; }
.form-panel h3 { margin: 0 0 5px; color: #243044; }
.form-panel p { margin: 0 0 18px; font-size: 12px; line-height: 1.6; }
.table-panel { min-width: 0; }
.form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 12px; }
.span-two { grid-column: span 2; }
.form-actions { justify-content: flex-end; margin-top: 14px; }
.drawer-form { padding: 0 4px; }
.drawer-actions { display: flex; justify-content: flex-end; gap: 10px; }
.drawer-secondary-action { margin-top: 14px; }
.history-table { margin-top: 20px; }
.table-pagination { display: flex; justify-content: flex-end; padding-top: 16px; }
.record-primary { color: #26334a; font-weight: 600; }
.record-secondary { margin-top: 3px; color: #7b8799; font-size: 12px; line-height: 1.5; }
.record-tags { display: flex; gap: 6px; margin-top: 6px; }
.member-tag { margin: 3px 5px 3px 0; }
:deep(.section-tabs > .el-tabs__header) { display: none; }
:deep(.el-select), :deep(.el-input-number) { width: 100%; }
@media (max-width: 1050px) { .summary-grid { grid-template-columns: repeat(2, 1fr); } .summary-grid article:nth-child(2) { border-right: 0; } .summary-grid article:nth-child(-n + 2) { border-bottom: 1px solid #edf0f4; } .split-layout, .double-forms { grid-template-columns: 1fr; } }
@media (max-width: 620px) { .summary-grid, .form-grid { grid-template-columns: 1fr; } .span-two { grid-column: auto; } }
</style>
