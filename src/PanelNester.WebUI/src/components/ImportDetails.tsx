import type {
  ImportConfiguration,
  ImportSourceMetadata,
  Material,
  OptimizationGroup,
  PartOverride,
  PartRow,
} from '../types/contracts';

interface ImportDetailsProps {
  importSource: ImportSourceMetadata;
  importConfiguration: ImportConfiguration;
  importedParts: PartRow[];
  materials: Material[];
  optimizationGroups: OptimizationGroup[];
}

function displayImportedValue(value: string | number | null | undefined): string {
  return value === null || value === undefined || value === '' ? 'Blank' : `${value}`;
}

function describePartOverride(partOverride: PartOverride): string {
  const imported = partOverride.importedValues;
  const current = partOverride.currentValues;
  const changes = [
    ['Part ID', imported.importedId, current.importedId],
    ['Length', imported.lengthText ?? imported.length, current.lengthText ?? current.length],
    ['Width', imported.widthText ?? imported.width, current.widthText ?? current.width],
    ['Quantity', imported.quantityText ?? imported.quantity, current.quantityText ?? current.quantity],
    ['Material', imported.materialName, current.materialName],
    ['Part Group', imported.group, current.group],
    ['Sheet Number', imported.sheetNumber, current.sheetNumber],
    ['Row Number', imported.rowNumber, current.rowNumber],
    ['Column Number', imported.columnNumber, current.columnNumber],
  ]
    .filter(([, before, after]) => before !== after)
    .map(
      ([label, before, after]) =>
        `${label}: ${displayImportedValue(before)} → ${displayImportedValue(after)}`,
    );
  const sources = partOverride.sourceReferences
    .map((reference) => `${reference.worksheetName}!${reference.physicalRow}`)
    .join(', ');
  return `${current.importedId || partOverride.rowId}: ${changes.join('; ') || 'No value changes recorded'} (${sources})`;
}

export function ImportDetails({
  importSource,
  importConfiguration,
  importedParts,
  materials,
  optimizationGroups,
}: ImportDetailsProps) {
  const sourceReferenceLabels = importedParts.flatMap((part) =>
    (part.sourceReferences ?? []).map(
      (reference) =>
        `${part.importedId || part.rowId}: ${reference.worksheetName}!${reference.physicalRow} (${reference.sourceFingerprint})`,
    ),
  );

  return (
    <details className="import-details">
      <summary>Import Details</summary>
      <div className="import-details__content">
        <dl className="import-details__metadata">
          <div>
            <dt>Import Source</dt>
            <dd>{importSource.importSourcePath}</dd>
          </div>
          <div>
            <dt>Content fingerprint</dt>
            <dd className="import-details__fingerprint">{importSource.contentFingerprint}</dd>
          </div>
          <div>
            <dt>Captured</dt>
            <dd>{new Date(importSource.snapshotCapturedAtUtc).toLocaleString()}</dd>
          </div>
          <div>
            <dt>Content length</dt>
            <dd>{importSource.contentLength.toLocaleString()} bytes</dd>
          </div>
        </dl>

        <section>
          <h2>Selected Worksheets</h2>
          {importConfiguration.worksheets.map((worksheet) => {
            const optimizationGroup = optimizationGroups.find(
              (group) => group.optimizationGroupId === worksheet.optimizationGroupId,
            );
            return (
              <article
                className="import-details__worksheet"
                key={`${worksheet.originalPosition}:${worksheet.worksheetName}`}
              >
                <h3>{worksheet.worksheetName}</h3>
                <p>
                  Position {worksheet.originalPosition} · Heading Range{' '}
                  {worksheet.headingRange || 'Not recorded'} · Optimization Group{' '}
                  {optimizationGroup?.name ?? worksheet.optimizationGroupId ?? 'Not assigned'}
                </p>
                <p>
                  Column Mappings:{' '}
                  {worksheet.columnMappings.length > 0
                    ? worksheet.columnMappings
                        .map((mapping) => `${mapping.sourceColumn} → ${mapping.targetField}`)
                        .join(', ')
                    : 'None'}
                </p>
                <p>
                  Excluded Source Rows:{' '}
                  {worksheet.excludedSourceRows.length > 0
                    ? worksheet.excludedSourceRows
                        .map(
                          (row) =>
                            `${row.sourceReference.worksheetName}!${row.sourceReference.physicalRow} (${row.sourceReference.sourceFingerprint}; ${row.originalValidationError.message})`,
                        )
                        .join(', ')
                    : 'None'}
                </p>
              </article>
            );
          })}
        </section>

        <section>
          <h2>Material Resolutions</h2>
          <p>
            {importConfiguration.options.materialMappings.length > 0
              ? importConfiguration.options.materialMappings
                  .map((mapping) => {
                    const material = materials.find(
                      (item) => item.materialId === mapping.targetMaterialId,
                    );
                    return `${mapping.sourceMaterialName} → ${material?.name ?? mapping.targetMaterialId}`;
                  })
                  .join(', ')
              : 'None'}
          </p>
        </section>

        <section>
          <h2>Source References</h2>
          <p>{sourceReferenceLabels.length > 0 ? sourceReferenceLabels.join(', ') : 'None'}</p>
        </section>

        <section>
          <h2>Part Overrides</h2>
          <p>
            {(importConfiguration.partOverrides ?? []).length > 0
              ? importConfiguration.partOverrides.map(describePartOverride).join(', ')
              : 'None'}
          </p>
        </section>
      </div>
    </details>
  );
}
