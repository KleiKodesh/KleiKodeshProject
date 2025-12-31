---
inclusion: fileMatch
fileMatchPattern: '**/*.csproj'
---

# VSTO Build Automation

## MSBuild Targets
Add build automation for HTML/Vue projects:

```xml
<Target Name="BuildHtmlApp" BeforeTargets="Build">
  <PropertyGroup>
    <HtmlProjectPath>$(MSBuildProjectDirectory)\..\[project-name]</HtmlProjectPath>
    <OutputHtmlFile>$(MSBuildProjectDirectory)\[Component]\[component]-index.html</OutputHtmlFile>
  </PropertyGroup>
  
  <Exec Command="npm run build" WorkingDirectory="$(HtmlProjectPath)" />
  <Copy SourceFiles="$(HtmlProjectPath)\dist\index.html" DestinationFiles="$(OutputHtmlFile)" />
</Target>
```

## Content Inclusion
```xml
<ItemGroup>
  <Content Include="[Component]\[component]-index.html" Condition="Exists('[Component]\[component]-index.html')">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

## Build Requirements
- **Always use `dotnet build`** - never msbuild directly
- **Automatic HTML builds** - MSBuild targets handle sub-project builds
- **File discovery** - WebViewBase auto-discovers HTML files