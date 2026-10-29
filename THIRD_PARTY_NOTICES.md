# 第三方说明

## 交互灵感：Tendedero

- 项目：https://github.com/alejandrobujan/tendedero
- 作者：Alejandro Buján
- 协议：https://github.com/alejandrobujan/tendedero/blob/main/LICENSE

RopeShelf使用独立C#实现及原创图标。本项目不分发Tendedero的Swift源码、名称作为产品品牌、图标或docs图片。如后续引入上游代码，须随该代码保留其版权声明及MIT许可，不得仅用本项目许可替换。

## .NET运行时

自包含便携包包括微软.NET及Windows Desktop运行时。它们及所包含组件的许可、版权与第三方声明位于发布包 `licenses/`。打包脚本从构建时实际恢复的运行时NuGet包中提取相关文件，不由本项目MIT许可取代。

## 开发工具

`scripts/make-icon.py` 使用Pillow生成本项目原创图标；Pillow是可选开发依赖，不随应用运行时分发。图标已包含在源码中，正常构建不需要运行此脚本。
