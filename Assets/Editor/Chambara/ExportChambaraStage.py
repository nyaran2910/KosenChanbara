"""Blender: --background --factory-startup --disable-autoexec --python this.py -- SOURCE.blend OUTPUT.fbx"""
import hashlib
import json
import os
import re
import sys

import bpy


source, output = map(os.path.abspath, sys.argv[sys.argv.index('--') + 1:])
bpy.ops.wm.open_mainfile(filepath=source)
bpy.context.scene.frame_set(1)
depsgraph = bpy.context.evaluated_depsgraph_get()
originals = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH' and obj.name != 'Studio_ground']
if not {'Platform_floor', 'Pale_red_rim', 'Center_logo_gray'}.issubset({obj.name for obj in originals}):
    raise RuntimeError('The supplied stage does not contain its floor, rim and logo.')

# Bake bevel/array modifiers and world transforms, retaining the supplied parts.
parts = []
used_materials = {}
for original in originals:
    evaluated = original.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(evaluated, depsgraph=depsgraph)
    mesh.transform(original.matrix_world)
    name = original.name
    original.name = 'Source_' + name
    part = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(part)
    parts.append(part)
    for material in mesh.materials:
        if material is None or material.name in used_materials:
            continue
        node = next(node for node in material.node_tree.nodes if node.type == 'BSDF_PRINCIPLED')
        safe_name = re.sub(r'[^A-Za-z0-9_]+', '_', material.name).strip('_')
        used_materials[material.name] = {
            'name': safe_name,
            'sourceName': material.name,
            'color': list(node.inputs['Base Color'].default_value),
            'roughness': node.inputs['Roughness'].default_value,
            'metallic': node.inputs['Metallic'].default_value,
        }

for source_name, entry in used_materials.items():
    bpy.data.materials[source_name].name = entry['name']
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
    'blenderVersion': bpy.app.version_string, 'meshes': len(parts),
    'materials': list(used_materials.values()),
    'excludedObjects': ['Studio_ground'],
}
with open(os.path.splitext(output)[0] + '.json', 'w', encoding='utf-8') as handle:
    json.dump(manifest, handle, indent=2)
    handle.write('\n')
print('EXPORTED CHAMBARA STAGE', len(parts), 'meshes;', output)
