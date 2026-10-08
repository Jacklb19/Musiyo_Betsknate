import { createHash } from 'node:crypto';
import { mkdir, readFile, realpath, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { draco, getBounds, simplify, weld } from '@gltf-transform/functions';
import draco3d from 'draco3dgltf';
import { MeshoptSimplifier } from 'meshoptimizer';
import sharp from 'sharp';

export const PROFILES = Object.freeze({
  web: { triangles: 130000, bytes: 10000000, textureSize: 2048 },
  quest: { triangles: 40000, bytes: 6000000, textureSize: 1024 },
});
const checkout = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const within = (root, file) => {
  const relative = path.relative(root, file);
  return relative === '' || (!relative.startsWith('..' + path.sep) && relative !== '..' && !path.isAbsolute(relative));
};
export function checkOutputLocation(file, source, repository = checkout) {
  if (within(repository, file)) throw new Error('Cultural model outputs must remain outside the repository.');
  if (file === source) throw new Error('The capture original must never be overwritten.');
}
export function triangleCount(document) {
  let count = 0;
  for (const mesh of document.getRoot().listMeshes())
    for (const primitive of mesh.listPrimitives()) {
      if (primitive.getMode() !== 4) throw new Error('Only triangle meshes are supported.');
      count += (primitive.getIndices() ?? primitive.getAttribute('POSITION')).getCount() / 3;
    }
  return count;
}
export async function createIO() {
  await MeshoptSimplifier.ready;
  return new NodeIO().registerExtensions(ALL_EXTENSIONS).registerDependencies({
    'draco3d.decoder': await draco3d.createDecoderModule(),
    'draco3d.encoder': await draco3d.createEncoderModule(),
  });
}
export async function prepareVariant(io, bytes, profile) {
  const limits = PROFILES[profile];
  if (!limits) throw new Error('Unknown target profile.');
  const document = await io.readBinary(bytes);
  const root = document.getRoot();
  if (root.listScenes().length !== 1 || root.listSkins().length || root.listAnimations().length
    || root.listCameras().length) throw new Error('Only a single static model scene is supported.');
  let instances = 0;
  root.listScenes()[0].traverse(node => { if (node.getMesh()) instances++; });
  if (instances !== root.listMeshes().length) throw new Error('Instanced or unreferenced meshes need manual review.');
  const originalTriangles = triangleCount(document);
  if (!Number.isInteger(originalTriangles) || originalTriangles < 1) throw new Error('Invalid triangle geometry.');
  const originalBounds = getBounds(root.listScenes()[0]);
  const reductions = [];
  if (originalTriangles > limits.triangles) {
    const ratio = (limits.triangles * .97) / originalTriangles;
    await document.transform(weld(), simplify({ simplifier: MeshoptSimplifier, ratio, error: .005, lockBorder: true }));
    reductions.push({ operation: 'simplify', ratio, maximum_relative_error: .005, lock_border: true });
  }
  for (const texture of root.listTextures()) {
    if (!['image/jpeg', 'image/png'].includes(texture.getMimeType())) throw new Error('Only embedded PNG/JPEG textures are supported.');
    const image = texture.getImage();
    if (!image) throw new Error('Missing embedded texture.');
    const metadata = await sharp(image).metadata();
    if (!metadata.width || !metadata.height) throw new Error('Invalid texture dimensions.');
    if (Math.max(metadata.width, metadata.height) > limits.textureSize) {
      let pipeline = sharp(image).resize(limits.textureSize, limits.textureSize, { fit: 'inside', withoutEnlargement: true });
      pipeline = texture.getMimeType() === 'image/jpeg' ? pipeline.jpeg({ quality: 92 }) : pipeline.png({ compressionLevel: 9 });
      texture.setImage(new Uint8Array(await pipeline.toBuffer()));
      reductions.push({ operation: 'resize_texture', original_size: [metadata.width, metadata.height], max_size: limits.textureSize });
    }
  }
  await document.transform(draco({ method: 'edgebreaker', quantizePosition: 14, quantizeNormal: 10, quantizeTexcoord: 12 }));
  const output = await io.writeBinary(document);
  const checked = await io.readBinary(output);
  const triangles = triangleCount(checked);
  if (triangles > limits.triangles || output.byteLength > limits.bytes)
    throw new Error(`Variant exceeds ${profile} limits: ${triangles} triangles, ${output.byteLength} bytes. Manual optimization is required.`);
  const bounds = getBounds(checked.getRoot().listScenes()[0]);
  const extent = Math.max(...originalBounds.max.map((value, index) => value - originalBounds.min[index]));
  if (!Number.isFinite(extent) || extent <= 0) throw new Error('Invalid model bounds.');
  for (let axis = 0; axis < 3; axis++)
    if (Math.abs(bounds.min[axis] - originalBounds.min[axis]) > extent * .01
      || Math.abs(bounds.max[axis] - originalBounds.max[axis]) > extent * .01)
      throw new Error('The model silhouette bounds changed by more than one percent. Manual review is required.');
  const textures = [];
  for (const texture of checked.getRoot().listTextures()) {
    const metadata = await sharp(texture.getImage()).metadata();
    if (Math.max(metadata.width, metadata.height) > limits.textureSize) throw new Error('Texture exceeds the target limit.');
    textures.push({ mime: texture.getMimeType(), width: metadata.width, height: metadata.height });
  }
  return { bytes: output, report: { profile, byte_count: output.byteLength, triangles, sha256: hash(output),
    original_triangles: originalTriangles, bounds, textures, reductions, draco_position_bits: 14,
    visual_fidelity_verified: false, device_verified: false, runtime_astc_verified: false } };
}

export async function generateVariants(sourceFile, outputDirectory, profiles = Object.keys(PROFILES)) {
  if (!profiles.length || new Set(profiles).size !== profiles.length || profiles.some(profile => !PROFILES[profile]))
    throw new Error('Unknown or repeated target profile.');
  const source = await realpath(sourceFile);
  // Resolve symlinks before any output creation so repository exclusions cannot be bypassed.
  const parent = await realpath(path.dirname(path.resolve(outputDirectory)));
  const destination = path.join(parent, path.basename(outputDirectory));
  checkOutputLocation(destination, source);
  const bytes = await readFile(source);
  const io = await createIO();
  const name = path.basename(source, '.glb');
  const variants = [];
  for (const profile of profiles) variants.push(await prepareVariant(io, bytes, profile));
  if (hash(await readFile(source)) !== hash(bytes)) throw new Error('The source changed during conversion.');
  await mkdir(destination, { recursive: false });
  for (const variant of variants) {
    const filename = name + '_' + variant.report.profile + '.glb';
    await writeFile(path.join(destination, filename), variant.bytes, { flag: 'wx' });
    variant.report.file = filename;
  }
  const report = { schema_version: 1, status: 'technical_candidate', created_at: new Date().toISOString(),
    source_file: source, source_sha256: hash(bytes), source_preserved: true,
    tools: JSON.parse(await readFile(new URL('./package.json', import.meta.url), 'utf8')).dependencies,
    publication_approved: false, curatorial_metadata: null, capture_paradata: null,
    limitations: ['Lossy mesh simplification and Draco quantization require visual review.',
      'Texture resizing does not establish ASTC runtime compression or device performance.',
      'This technical report does not replace capture provenance, narration or the curatorial record.'],
    variants: variants.map(variant => variant.report) };
  await writeFile(path.join(destination, 'technical-paradata.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
  return report;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (![4, 5].includes(process.argv.length)) { console.error('Usage: node variants.mjs <source.glb> <new-external-directory> [web|quest]'); process.exitCode = 2; }
  else try { console.log(JSON.stringify(await generateVariants(process.argv[2], process.argv[3], process.argv[4] ? [process.argv[4]] : undefined), null, 2)); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
