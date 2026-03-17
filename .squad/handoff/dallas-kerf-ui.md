# UI Requirements: Kerf Width Setting

## Backend Status: ✅ Complete

The backend now supports editable kerf width through project settings. All changes are complete and tested.

## Frontend Work Required

### Add Kerf Width Input Control

**Location**: Project settings/metadata page (wherever project-level settings are edited)

**Specification**:
```tsx
<label>
  Kerf Width (inches)
  <input
    type="number"
    min="0"
    step="0.0625"
    value={projectSettings.kerfWidth}
    onChange={(e) => handleKerfWidthChange(parseFloat(e.target.value) || 0)}
  />
</label>
```

**Validation**:
- Must be `>= 0` (backend clamps negative values to default)
- Defaults to `0.0625` for new projects
- Step of `0.0625` (1/16 inch) is standard but not required

### Persist Changes

Wire the input to the existing `updateProjectMetadata` bridge call:

```typescript
const response = await hostBridge.invoke<UpdateProjectMetadataResponse>(
  bridgeMessageTypes.updateProjectMetadata,
  {
    project: currentProject,
    metadata: currentProject.metadata,
    settings: {
      kerfWidth: newKerfValue,  // from user input
      reportSettings: currentProject.settings.reportSettings
    }
  }
);

// Update local state with returned project
if (response.success && response.project) {
  setCurrentProject(response.project);
}
```

### Notes

- The backend already reads `projectSettings.kerfWidth` when running nesting (see `App.tsx:2099`)
- No bridge contract changes needed—everything flows through existing `ProjectSettings` interface
- The value persists automatically when saving the project
- Reopening a project restores the saved kerf width

## Example Placement

Consider adding it near report settings or in a "Nesting Options" section of the project metadata page. The kerf width affects nesting results, so users should be able to edit it before running nesting operations.

## Testing

After implementation:
1. Create a new project → verify kerf defaults to 0.0625
2. Change kerf to 0.125 → save project → reopen → verify it persists at 0.125
3. Run nesting with custom kerf → verify layout spacing reflects the new kerf value
