# Proxy.Mesh
## EN:
**Replacement of the Skinned Mesh Renderer. Allows you to add additional passages for the geometry of the grid.**

## RU:
**Замена Skinned Mesh Renderer. Позволяет добавлять дополнительные проходы геометрии.**

## Основная идея.
**Мне хотелось создать такой же SkinnedMeshRenderer, какой есть в Unity, но с возможностью напрямую управлять мешем.**
**Плагин позволяет добавлять полностью собственные проходы геометрии и встраивать их в процесс рендеринга меша.**
**Полностью на C# без шейдеров, используется Job System + Burst Compiler**
