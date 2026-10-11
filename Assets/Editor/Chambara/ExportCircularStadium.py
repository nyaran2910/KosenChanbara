"""Blender: --background --factory-startup --disable-autoexec --python this.py -- SOURCE.blend OUTPUT.fbx"""
import collections
import hashlib
import json
import math
import os
import re
import sys

import bpy


def safe_name(name):
    return re.sub(r'[^A-Za-z0-9_]+', '_', name).strip('_')


source, output = map(os.path.abspath, sys.argv[sys.argv.index('--') + 1:])
bpy.ops.wm.open_mainfile(filepath=source)
bpy.context.scene.frame_set(1)
depsgraph = bpy.context.evaluated_depsgraph_get()
originals = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
if not {'Infield concrete foundation', 'Green infield', 'Pitch underlay', 'Grass mowing stripe 00'}.issubset({obj.name for obj in originals}):
    raise RuntimeError('The supplied stadium does not contain its playable grass field.')

# Preserve the supplied geometry, including evaluated modifiers. Batch seats,
# stands and roof by material and sector so the 1,414 source objects do not each
# require a renderer. Keep the field parts named for floor/boundary validation.
groups = collections.defaultdict(list)
materials = {}
for original in originals:
    evaluated = original.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(evaluated, depsgraph=depsgraph)
    mesh.transform(original.matrix_world)
    name = original.name
    original.name = 'Source_' + name
    part = bpy.data.objects.new(safe_name(name), mesh)
    bpy.context.collection.objects.link(part)
    for material in mesh.materials:
        if material is None or material.name in materials:
            continue
        node = next(node for node in material.node_tree.nodes if node.type == 'BSDF_PRINCIPLED')
        materials[material.name] = {
            'name': safe_name(material.name), 'sourceName': material.name,
            'color': list(node.inputs['Base Color'].default_value),
            'roughness': node.inputs['Roughness'].default_value,
            'metallic': node.inputs['Metallic'].default_value,
            'emission': list(node.inputs['Emission Color'].default_value),
            'emissionStrength': node.inputs['Emission Strength'].default_value,
        }
    if name in {'Infield concrete foundation', 'Green infield', 'Pitch underlay'} or name.startswith('Grass mowing stripe'):
        key = ('Field', safe_name(name))
    else:
        center = original.matrix_world.translation
        sector = int((math.atan2(center.y, center.x) + math.pi) * 8 / (2 * math.pi)) % 8
        key = ('Stadium_sector_' + str(sector), '_'.join(safe_name(m.name) for m in mesh.materials if m))
    groups[key].append(part)

for source_name, entry in materials.items():
    bpy.data.materials[source_name].name = entry['name']

parts = []
for (group, name), group_parts in groups.items():
    chunks, chunk, count = [], [], 0
    for part in group_parts:
        size = len(part.data.vertices)
        if chunk and count + size > 60000:
            chunks.append(chunk)
            chunk, count = [], 0
        chunk.append(part)
        count += size
    if chunk:
        chunks.append(chunk)
    for index, chunk in enumerate(chunks):
        bpy.ops.object.select_all(action='DESELECT')
        for part in chunk:
            part.select_set(True)
        bpy.context.view_layer.objects.active = chunk[0]
        if len(chunk) > 1:
            bpy.ops.object.join()
        part = bpy.context.view_layer.objects.active
        part.name = name if group == 'Field' else group + '_' + name + '_' + str(index)
        parts.append(part)

bpy.ops.object.select_all(action='DESELECT')
for part in parts:
    part.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
os.makedirs(os.path.dirname(output), exist_ok=True)
bpy.ops.export_scene.fbx(
    filepath=output, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
    global_scale=1.0, axis_forward='-Z', axis_up='Y', bake_space_transform=True,
    bake_anim=False, path_mode='STRIP', mesh_smooth_type='FACE')
with open(source, 'rb') as handle:
    digest = hashlib.sha256(handle.read()).hexdigest()
manifest = {
    'source': os.path.basename(source), 'sourceSha256': digest,
    'blenderVersion': bpy.app.version_string, 'sourceMeshes': len(originals),
    'meshes': len(parts), 'vertices': sum(len(part.data.vertices) for part in parts),
    'materials': list(materials.values()), 'excludedObjects': [],
}
with open(os.path.splitext(output)[0] + '.json', 'w', encoding='utf-8') as handle:
    json.dump(manifest, handle, indent=2)
    handle.write('\n')
print('EXPORTED CIRCULAR STADIUM', len(parts), 'meshes;', output)
