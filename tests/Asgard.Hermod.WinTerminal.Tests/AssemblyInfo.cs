// 原生焦点和输入法属于共享桌面状态，终端窗口用例不能并行争用。
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
