# ObservableCollections 在 CsWinRT/UWP 下的兼容性问题与修复说明

## 背景

项目将原先使用的 .NET `ObservableCollection<T>` 全量替换为 Cysharp `ObservableCollections`，主要使用：

```csharp
ObservableList<T>
```

以及官方提供的集合视图：

```csharp
collection.ToNotifyCollectionChanged()
```

同时，原先通过循环逐项调用 `Add` 的代码被替换为 `AddRange`，以降低批量添加数据时的开销。

迁移完成后，部分集合在普通 CLR 代码中可以正常工作，但进入 UWP XAML/CsWinRT 边界时会抛出：

```text
System.ArgumentException: 参数错误。 (0x80070057 / E_INVALIDARG)
```

## 结论

问题并不是 `ObservableList<T>` 或 `AddRange` 本身无法运行，而是 `ObservableCollections` 返回的第三方泛型集合视图在传入 XAML/WinRT 时，没有被 CsWinRT 自动生成为实际运行时类型所需的 CCW/vtable 元数据。

原生 `ObservableCollection<T>` 属于 .NET/CsWinRT 已知的标准集合类型，CsWinRT 已经具备相应的 WinRT 接口映射和投影支持；`ObservableCollections` 内部的泛型视图类型不属于这组内建映射，因此在 AOT 和禁用运行时封送的环境下必须显式声明需要暴露给 WinRT 的闭合泛型类型。

通过 `GeneratedWinRTExposedExternalType` 标记这些具体类型后，CsWinRT 可以在编译时生成相应的 CCW/vtable，XAML 随后便能够正确查询和调用集合接口。

更准确地说，这次修复补齐的是第三方 CLR 类型暴露给 WinRT 所需的接口实现和虚表注册，而不只是通常意义上的类型名称转换。

## 为什么旧版 ObservableCollection 可以正常工作

旧实现使用：

```csharp
ObservableCollection<T>
```

它能够正常工作的主要原因是：

1. `ObservableCollection<T>` 是框架标准集合类型。
2. CsWinRT 对常用框架集合以及 `INotifyCollectionChanged` 等接口存在已知的投影路径。
3. XAML 在访问集合时，可以取得它所需的 WinRT 集合接口和变更通知接口。
4. 项目启用 AOT 后，这些内建类型仍然具有编译期可用的封送支持。

迁移后，代码表面上返回的是：

```csharp
NotifyCollectionChangedSynchronizedViewList<T>
```

但 `ToNotifyCollectionChanged()` 实际创建的具体对象通常是：

```csharp
NonFilteredSynchronizedViewList<T, T>
```

该类型是第三方库内部的泛型实现。CsWinRT 源生成器没有自动发现项目运行时会把哪些闭合泛型实例传给 XAML，因此生成的全局 vtable 查找表中没有这些类型。

当 XAML 尝试查询集合、可枚举或集合变更通知接口时，缺少对应的 CCW/vtable，错误最终被 WinRT 转换为较为宽泛的：

```text
E_INVALIDARG (0x80070057)
```

因此，异常信息虽然显示“参数错误”，实际原因并不一定是调用方传入了错误参数，而可能是 WinRT 无法为该 CLR 实例取得预期接口。

## 修复过程

### 1. 使用 ObservableCollections 官方实现

集合统一使用 `ObservableList<T>`，需要传给 XAML 的位置直接使用官方扩展方法：

```csharp
var items = new ObservableList<Item>();
var itemsView = items.ToNotifyCollectionChanged();
```

没有增加转发集合、自定义代理集合或在两个集合之间同步数据的逻辑。

这样可以继续使用 `ObservableCollections` 的批量操作能力，同时避免维护两套集合状态。

### 2. 使用 AddRange 替代循环 Add

原先的逐项添加：

```csharp
foreach (var item in newItems)
{
    items.Add(item);
}
```

替换为：

```csharp
items.AddRange(newItems);
```

`ToNotifyCollectionChanged()` 当前使用默认的非范围通知模式。底层可以通过 `AddRange` 高效修改集合，而面向 UWP/XAML 的视图会生成兼容的集合变更通知，避免直接向不支持范围通知的控件发送多项 `Add` 事件。

### 3. 为参与 CsWinRT 生成的类型补充 partial

CsWinRT 的源生成机制要求相关类型能够参与生成，因此为集合视图和增量加载集合等相关声明补充了 `partial`。

需要注意：

> `partial` 是允许源生成器补充代码的必要条件之一，但只添加 `partial` 并不能确保所有第三方闭合泛型类型都会被自动发现和注册。

这也是第一次仅补充 `partial` 后仍然出现 `E_INVALIDARG` 的原因。

### 4. 确认真正进入 XAML 的运行时类型

进一步检查生成程序集后发现，CsWinRT 的全局 vtable 查找表中并未包含：

```csharp
NonFilteredSynchronizedViewList<T, TView>
```

而该类型正是 `ToNotifyCollectionChanged()` 返回视图的实际实现之一。

因此，将该实现调整为主项目可引用的公开类型：

```csharp
public sealed partial class NonFilteredSynchronizedViewList<T, TView>
    : NotifyCollectionChangedSynchronizedViewList<TView>
```

公开该类型不是为了让业务代码直接构造它，而是为了让主应用程序集能够在 CsWinRT 特性中准确引用实际运行时类型。

### 5. 自动生成闭合泛型类型注册

`ObservableCollections.SourceGenerator` 会扫描 `ToNotifyCollectionChanged()`、`ToViewList()` 调用，以及实际构造的 `ISupportIncrementalLoading` 类型，并生成程序集级特性，例如：

```csharp
[assembly: GeneratedWinRTExposedExternalType(
    typeof(NonFilteredSynchronizedViewList<CommentBase, CommentBase>))]
```

这里不能使用普通 Roslyn Source Generator 与 CsWinRT 在同一轮生成：同一轮中的生成器看不到其他生成器刚添加的源码。当前项目因此通过 `BeforeTargets="CoreCompile"` 的 MSBuild 步骤预先分析源码，将结果写入：

```text
$(IntermediateOutputPath)ObservableCollections.SourceGenerator/
    ObservableCollections.GeneratedWinRTExposedExternalTypes.g.cs
```

该文件会在 C# 编译器和 CsWinRT Source Generator 启动前加入 `@(Compile)`。预编译工具不存在、扫描失败或文件无法生成时，MSBuild 会直接失败，不会继续产生缺少 WinRT 暴露信息的应用程序集。

项目中所有实际传入 XAML 的元素类型仍会分别注册，因为：

```csharp
NonFilteredSynchronizedViewList<CommentBase, CommentBase>
```

和：

```csharp
NonFilteredSynchronizedViewList<DownloadObject, DownloadObject>
```

在运行时是两个不同的闭合泛型类型，必须具有各自的生成信息。

同理，跨越 WinRT 边界并实现增量加载接口的集合也会被自动注册，例如：

```csharp
[assembly: GeneratedWinRTExposedExternalType(
    typeof(IncrementalLoadingCollection<CommentSource, CommentBase>))]
```

这些标记使 CsWinRT 在编译期生成对应的 CCW/vtable，并将其加入查找表。XAML 查询集合接口时便不再依赖缺失的运行时动态封送能力。

## DependencyProperty 的单独处理

`GeneratedWinRTExposedExternalType` 解决的是：

> 一个具体的 CLR 对象实例如何以所需接口暴露给 WinRT。

它并不表示任意 CLR 泛型类型都适合作为 DependencyProperty 的元数据类型。

例如，下面的注册仍然可能在 WinRT 类型系统中失败：

```csharp
DependencyProperty.Register(
    nameof(Comments),
    typeof(NotifyCollectionChangedSynchronizedViewList<CommentBase>),
    typeof(CommentsList),
    new PropertyMetadata(null));
```

原因是 DependencyProperty 注册时需要将属性类型转换为 WinRT 可识别的类型元数据，而第三方 CLR 泛型集合类型不一定能作为合法的 WinRT `TypeName` 使用。这与对象实例已有 CCW 是两个不同层面的问题。

### 当前处理方式

对于仅通过编译型 `x:Bind` 传入的集合，改为使用普通的强类型 CLR 属性：

```csharp
public NotifyCollectionChangedSynchronizedViewList<CommentBase>? Comments
{
    get => _comments;
    set
    {
        _comments = value;
        CommentsContainer.ItemsSource = value;
    }
}
```

父级 XAML 仍然可以正常传参：

```xml
<ui:CommentsList Comments="{x:Bind _floorCommentsView}" />
```

编译后的 `x:Bind` 会直接调用普通属性 setter，其效果可以近似理解为：

```csharp
commentsList.Comments = _floorCommentsView;
```

因此，删除 DependencyProperty 不会导致 `x:Bind` 无法传值。集合后续发生变化时，视图仍然通过 `INotifyCollectionChanged` 通知内部列表控件。

只有在以下场景中，目标属性才确实需要 DependencyProperty：

- 使用传统 `{Binding}` 作为目标属性；
- 需要在 `Style`/`Setter` 中设置属性；
- 需要动画系统修改属性；
- 需要属性值继承或 DependencyProperty 元数据回调；
- 外部代码需要通过 `GetValue`/`SetValue` 操作属性。

如果未来确实需要这些能力，应优先使用 WinRT 原生可识别的接口或非泛型类型作为 DependencyProperty 元数据类型，或者谨慎使用 `object` 作为 DP 存储层并在控件内部进行受控的强类型校验。不要直接把第三方 CLR 泛型集合注册为 DependencyProperty 类型。

## 根因与修复关系

| 层面 | 原因 | 修复 |
| --- | --- | --- |
| 集合实现 | 需要使用 `ObservableCollections` 和批量操作 | 使用 `ObservableList<T>`、`AddRange` 和官方 `ToNotifyCollectionChanged()` |
| XAML 实例封送 | 第三方闭合泛型视图没有自动进入 CsWinRT vtable 查找表 | 补充 `partial`、公开实际实现，并由 SourceGenerator 生成 `GeneratedWinRTExposedExternalType` |
| 增量加载 | 闭合泛型集合会跨越 WinRT 接口边界 | SourceGenerator 自动注册实际构造的 `IncrementalLoadingCollection<TSource, TItem>` |
| DependencyProperty | CLR 泛型类型不适合作为 WinRT DP 类型元数据 | 仅使用 `x:Bind` 的属性改为普通强类型 CLR 属性 |
| UWP 集合通知 | 部分 UWP 控件不接受范围集合变更事件 | 保持视图的非范围通知兼容模式，底层继续使用 `AddRange` |

## 后续维护注意事项

### 新增集合元素类型时

如果新增了下面的代码，并且该视图会进入 XAML/WinRT：

```csharp
ObservableList<NewItem> items = new();
var view = items.ToNotifyCollectionChanged();
```

`ObservableCollections.SourceGenerator` 会自动生成：

```csharp
[assembly: GeneratedWinRTExposedExternalType(
    typeof(NonFilteredSynchronizedViewList<NewItem, NewItem>))]
```

否则新类型可能再次在运行时出现 `0x80070057`。

### 新增增量加载集合时

如果新的闭合 `IncrementalLoadingCollection<TSource, TItem>` 会作为 `ISupportIncrementalLoading`、`ItemsSource` 或其他 WinRT 接口进入 XAML，生成器会从对象构造表达式中发现并注册对应类型。

当前增量加载实现位于 `ObservableCollections/`，以 Windows Community Toolkit 的 `IncrementalLoadingCollection` 为基础，并针对本项目做了以下适配：

- 集合基类改为 `ObservableList<T>`；
- 保留 `GetPagedItemsAsync(pageIndex, pageSize)`、空页结束、加载回调与 `RefreshAsync()` 行为；
- 只提供传入数据源实例的 AOT 安全构造路径，不使用反射创建数据源。

HyPlayer 层只保留面向播放列表容器和评论接口的 `IIncrementalSource<T>` 适配器，以及换源时所需的应用级取消逻辑；集合和加载行为统一使用 `ObservableCollections.IncrementalLoadingCollection<TSource, TItem>`。

### 调整视图创建参数时

如果以后改用筛选、投影或其他 `ObservableCollections` 视图配置，实际运行时类型可能不再是 `NonFilteredSynchronizedViewList<T, T>`。此时需要重新确认 `ToNotifyCollectionChanged()` 返回的具体实现，并注册真正跨越边界的类型。

### 出现 E_INVALIDARG 时的排查顺序

遇到新的：

```text
System.ArgumentException: 参数错误。 (0x80070057)
```

建议依次检查：

1. 异常是否发生在将对象设置给 XAML 属性或 `ItemsSource` 时；
2. 对象的实际运行时类型，而不是变量声明的基类或接口类型；
3. 该类型是否为第三方或自定义闭合泛型类型；
4. 类型是否满足 CsWinRT 源生成要求；
5. `GeneratedWinRTExposedExternalType` 是否注册了完全一致的闭合泛型类型；
6. 是否错误地把 CLR 泛型类型用于 DependencyProperty 元数据；
7. 集合是否向 UWP 控件发送了不受支持的范围变更通知。

## 最终概括

本次问题可以概括为：

> 原生 `ObservableCollection<T>` 具有 CsWinRT 内建的映射和封送支持；迁移到 `ObservableCollections` 后，实际进入 XAML 的第三方泛型集合视图没有被 CsWinRT 自动识别。在 AOT/禁用运行时封送的环境中，这导致 XAML 无法取得所需集合接口，并以 `E_INVALIDARG` 的形式失败。通过公开实际实现并由 `ObservableCollections.SourceGenerator` 标记使用到的闭合泛型类型，补齐 CsWinRT 的 CCW/vtable 生成，同时避免将第三方 CLR 泛型类型直接注册为 DependencyProperty 元数据，问题得以解决。
