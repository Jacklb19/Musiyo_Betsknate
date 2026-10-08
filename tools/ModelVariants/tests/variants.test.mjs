import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { Document } from '@gltf-transform/core';
import sharp from 'sharp';
import { checkOutputLocation, createIO, generateVariants, prepareVariant } from '../variants.mjs';

async function fixture(io, divisions = 1, textureWidth = 32) {
  const document = new Document();
  const buffer = document.createBuffer();
  const positions = [], uv = [], indices = [];
  for (let z = 0; z <= divisions; z++) for (let x = 0; x <= divisions; x++) {
    positions.push(x / divisions, 0, z / divisions);
    uv.push(x / divisions, z / divisions);
  }
  for (let z = 0; z < divisions; z++) for (let x = 0; x < divisions; x++) {
    const index = z * (divisions + 1) + x;
    indices.push(index, index + divisions + 1, index + 1, index + 1, index + divisions + 1, index + divisions + 2);
  }
  const accessor = (type, array) => document.createAccessor().setType(type).setArray(array).setBuffer(buffer);
  const image = await sharp({ create: { width: textureWidth, height: Math.ceil(textureWidth / 2), channels: 3,
    background: { r: 40, g: 120, b: 60 } } }).png().toBuffer();
  const texture = document.createTexture().setMimeType('image/png').setImage(image);
  const material = document.createMaterial().setBaseColorTexture(texture);
  const primitive = document.createPrimitive().setAttribute('POSITION', accessor('VEC3', new Float32Array(positions)))
    .setAttribute('TEXCOORD_0', accessor('VEC2', new Float32Array(uv)))
    .setIndices(accessor('SCALAR', new Uint32Array(indices))).setMaterial(material);
  const mesh = document.createMesh().addPrimitive(primitive);
  document.createScene().addChild(document.createNode().setMesh(mesh));
  return io.writeBinary(document);
}

test('output guards preserve originals and exclude the checkout without rejecting siblings', () => {
  const root = path.resolve('repository');
  assert.throws(() => checkOutputLocation(path.join(root, 'output'), 'original', root), /outside/);
  assert.throws(() => checkOutputLocation('/same', '/same', root), /overwritten/);
  assert.doesNotThrow(() => checkOutputLocation(path.resolve('repository-variants/output'), 'original', root));
});
test('Quest output is really simplified, decoded and resized within limits', async () => {
  const io = await createIO();
  const input = await fixture(io, 160, 2050);
  const variant = await prepareVariant(io, input, 'quest');
  assert.equal(variant.report.original_triangles, 51200);
  assert.ok(variant.report.triangles <= 40000 && variant.report.triangles > 0);
  assert.ok(variant.report.byte_count <= 6000000);
  assert.equal(variant.report.textures[0].width, 1024);
  assert.equal(variant.report.visual_fidelity_verified, false);
  assert.equal(variant.report.device_verified, false);
  const document = await io.readBinary(variant.bytes);
  assert.ok(document.getRoot().listExtensionsUsed().some(extension => extension.extensionName === 'KHR_draco_mesh_compression'));
  await assert.rejects(prepareVariant(io, input, 'unknown'), /Unknown/);
});
test('generation preserves the source hash, records limits and refuses an existing directory', async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'musiyo-model-test-'));
  try {
    const io = await createIO();
    const input = await fixture(io);
    const source = path.join(directory, 'neutral.glb');
    await writeFile(source, input);
    const output = path.join(directory, 'variants');
    const report = await generateVariants(source, output);
    assert.deepEqual(await readFile(source), Buffer.from(input));
    assert.equal(report.source_preserved, true);
    assert.equal(report.publication_approved, false);
    assert.equal(report.curatorial_metadata, null);
    assert.equal(report.variants.length, 2);
    assert.equal(JSON.parse(await readFile(path.join(output, 'technical-paradata.json'), 'utf8')).source_sha256, report.source_sha256);
    await assert.rejects(generateVariants(source, output), /EEXIST/);
  } finally { await rm(directory, { recursive: true, force: true }); }
});
