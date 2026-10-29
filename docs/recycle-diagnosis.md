# 回收站检查记录

2026-10-28。可重复命令：`RopeShelf.Tests.exe <fixture-root> --recycle`。仅使用程序生成的测试图片。

- IFileOperation 调用完成，文件离开源路径；PostDeleteItem 的 HRESULT 为 `0x00270008`，目标对象为空，不能证明文件已进入回收站。
- Shell.Application 的回收站命名空间可访问，但未发现此次生成文件的原始位置记录。
- 加入 FOF_ALLOWUNDO 后结果不变；缩短路径后结果不变，原始路径本身也未超过260字符。
- 全局 Advise 与逐项回调均未增加回收站目标证据。
- 早期独立探测错误地绑定到文件，其 `E_NOTIMPL` 结果不具备判断环境能力的意义。
- 2026-10-29 按文档从父文件夹绑定 ITransferSource，调用 RecycleItem 返回 `0x80270021`（COPYENGINE_E_ACCESS_DENIED_SRC，源访问安全错误），目标对象为空。源文件由测试备份恢复。此探测仍未找到可验证的成功路径，不能据此断言普通桌面环境缺少接口实现。
- 同日只读检查：IsTokenRestricted=false，测试源文件所有者为当前用户、属性Archive且路径可读取。这排除了“已确认受限令牌”这一解释，仍不能推导Shell操作权限正常或定位实际拒绝原因。没有修改系统安全设置。
- 注册表检查未发现 NukeOnDelete 开启；未修改任何系统回收站设置。

目前能证实的是本执行环境内两种调用均未通过回收证据检查，具体权限原因尚未查明。不能据此断言普通桌面环境一定失败或成功。保留原有验证标准，不把源文件消失当成成功。恢复副本确保当前检查失败后原图片仍存在。

无效替代方案和临时探测代码已从生产路径移除。回收站成功路径仍需在具备完整 Shell 回收能力的 Windows 桌面复核；其他开发工作继续推进。

依据：[PostDeleteItem 目标对象语义](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-postdeleteitem)、[RecycleItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itransfersource-recycleitem)。
