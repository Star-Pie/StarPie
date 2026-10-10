# 桌宠兼容与插件设置控件

当前修复候选在 beta.7 主分支基础上提供 SDK 1.11，并保留已经存在的 SDK 1.10 交互事件。SDK 程序集身份仍为 `StarPie.Plugin.Abstractions, Version=1.0.0.0`；旧接口、旧枚举值与默认行为不变。使用以下新增成员的插件应声明 `apiVersion: "1.11"`。旧 beta.7 资源中的 SDK 1.10 不包含这些成员，仅修改插件清单不能修复兼容性。

## 新增声明

- `ParameterFieldType.Slider = 10`：使用 `Min`、`Max`、有限正数 `Step`（默认 1）及可选 `Unit`。宿主显示减/加按钮与数值，按 `InvariantCulture` 存储；无效或超范围值由既有参数校验报告。控件回填只显示，不回写、落盘或触发动作。
- `ParameterField.HelpTextKey` 与 `ActionDescriptor.DescriptionKey`：优先解析本插件词条，缺词条时回退原字面说明。
- `ActionDescriptor.ShowInActionPicker`：默认为 true。设为 false 时隐藏新选择；已有动作引用仍可编辑和执行，不清空旧配置。
- `SettingsPageDescriptor.ActionIds`：引用同插件已经注册的动作短 ID。宿主在提交时校验动作存在，复制列表，执行时重新检查实例状态和代际，再通过既有动作队列处理。
- `SettingsPageDescriptor.Sections` 与 `SettingsSectionDescriptor`：分区以唯一 ID 和已声明字段键构成。字段最多属于一个分区，分区不可为空；主设置页不重复显示这些字段。分区复用同一插件设置存储，关闭时按现有规则落盘，旧实例的页面不能写回重载后的设置。

不新增权限、外部依赖或自定义插件 UI；插件不得把管理操作回调或窗口对象放入声明。宿主不会因这个修复重新启用用户禁用的插件，也不替换桌宠数据、清单或素材。

## 验证与验收

`scratch/desktop-pet-compatibility-tests` 使用指定实际 DLL 完成静态识别、隔离安装/初始化/提交与卸载，不执行动作、不创建桌宠窗口。`scratch/plugin-controls-tests` 使用 SDK-only 严格禁止动作调用的夹具，验证设置命令、隐藏引用、滑块回填/步进、分区与代际。

轮盘会话验证器可通过 `STARPIE_DESKTOP_PET_DLL` 选择同一 DLL，仍保留原有断言。不要为运行测试编译或改写他人的插件开发工作树。

真实桌宠显示、点击唤出轮盘、方向姿势、拖动、素材切换与设置窗口必须人工验收。源码候选、已安装主程序及旧本地资源是不同对象，验收后再明确合并和重建资源。
