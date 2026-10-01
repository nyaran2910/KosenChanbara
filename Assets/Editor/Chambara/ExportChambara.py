"""Blender 5.2: --background --factory-startup --disable-autoexec --python this.py -- SOURCE.blend OUTPUT_DIR"""
import bpy
import json
import os
import sys
import math
from mathutils import Matrix, Vector

source, output = sys.argv[sys.argv.index('--') + 1:]
os.makedirs(output, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(source))
if bpy.context.object is not None and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.scene.frame_set(30)
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()

def vertices(ob):
    ev = ob.evaluated_get(dg)
    mesh = ev.to_mesh()
    points = [ev.matrix_world @ v.co for v in mesh.vertices]
    ev.to_mesh_clear()
    return points

def center(points):
    return Vector([(min(p[i] for p in points) + max(p[i] for p in points)) / 2 for i in range(3)])

def srgb(v):
    return v * 12.92 if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055

materials = []
for mat in bpy.data.materials:
    node = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if mat.node_tree else None
    color = list(node.inputs['Base Color'].default_value) if node else list(mat.diffuse_color)
    materials.append({'name': mat.name, 'color': [srgb(v) for v in color[:3]] + [color[3]],
                      'roughness': node.inputs['Roughness'].default_value if node else 0.6})
manifest = {'frame': 30, 'bodyHeight': 2.0, 'gripToTip': 2.1, 'materials': materials, 'characters': []}
# Unity's baked FBX conversion maps Blender +Y to model +Z. Rotate the
# source's -Y-facing geometry without reflecting its normals or winding.
unity_forward = Matrix.Rotation(math.pi, 3, 'Z')

# Bake the evaluated frame-30 geometry and pose into a weighted rest rig.
# Keep source vertex-group indices: subdivision interpolates the original weights.
# The source blend is read only; gloves, display objects and animation are excluded.
for color, collection in [('Red', '01_RED_character'), ('Blue', '02_BLUE_character')]:
    prefix = color.upper() + '_'
    objects = [ob for ob in bpy.data.collections[collection].all_objects if ob.type == 'MESH']
    swords = [ob for ob in objects if ob.name.startswith(prefix + 'Sword_') or ob.name.startswith(prefix + 'Grip_wrap')]
    bodies = [ob for ob in objects if ob not in swords and not ob.name.startswith(prefix + 'Glove')]
    body_points = [p for ob in bodies for p in vertices(ob)]
    floor = min(p.z for p in body_points)
    scale = 2.0 / (max(p.z for p in body_points) - floor)
    root = bpy.data.objects[prefix + 'Character'].matrix_world.translation
    body_origin = Vector((root.x, root.y, floor))
    chest = center(vertices(bpy.data.objects[prefix + 'Chest_guard']))
    chest_center = [0.0, (chest.z - floor) * scale - 1.0, 0.0]
    chest_front = max(-p.y + root.y for ob in bodies if 'Chest_' in ob.name for p in vertices(ob)) * scale
    grip = center(vertices(bpy.data.objects[prefix + 'Sword_grip']))
    tip = center(vertices(bpy.data.objects[prefix + 'Sword_rounded_tip']))
    axis = (tip - grip).normalized()
    sword_points = [p for ob in swords for p in vertices(ob)]
    sword_scale = 2.1 / max((p - grip).dot(axis) for p in sword_points)
    sword_rotation = axis.rotation_difference(Vector((0, -1, 0))).to_matrix()
    manifest['characters'].append({'name': color, 'chestCenter': chest_center,
                                    'guardPlaneOffset': chest_front + 0.08})
    source_rig = bpy.data.objects[prefix + 'Rig']
    rig_data = bpy.data.armatures.new(prefix + 'RestSkeleton')
    rig = bpy.data.objects.new(prefix + 'Rig', rig_data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for bone in source_rig.pose.bones:
        if bone.name == 'shadow_model':
            continue
        rest = rig_data.edit_bones.new(bone.name)
        world = source_rig.matrix_world @ bone.matrix
        head = unity_forward @ ((world.translation - body_origin) * scale)
        rotation = (unity_forward @ world.to_3x3()).normalized().to_quaternion()
        rest.matrix = Matrix.LocRotScale(head, rotation, Vector((1, 1, 1)))
        rest.length = max(0.01, bone.length * source_rig.matrix_world.to_scale().x * scale)
    for bone in source_rig.pose.bones:
        if bone.name in rig_data.edit_bones and bone.parent and bone.parent.name in rig_data.edit_bones:
            rig_data.edit_bones[bone.name].parent = rig_data.edit_bones[bone.parent.name]
    bpy.ops.object.mode_set(mode='OBJECT')
    for part, originals in [('Body', bodies), ('Sword', swords)]:
        baked = []
        for ob in originals:
            ev = ob.evaluated_get(dg)
            mesh = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
            for vertex in mesh.vertices:
                world = ev.matrix_world @ vertex.co
                local = (world - body_origin) * scale if part == 'Body' else sword_rotation @ ((world - grip) * sword_scale)
                vertex.co = unity_forward @ local
            mesh.update()
            obj = bpy.data.objects.new('Baked_' + ob.name, mesh)
            bpy.context.scene.collection.objects.link(obj)
            if part == 'Body':
                for group in ob.vertex_groups:
                    obj.vertex_groups.new(name=group.name)
                modifier = obj.modifiers.new('Rest skin', 'ARMATURE')
                modifier.object = rig
                obj.parent = rig
            baked.append(obj)
        bpy.ops.object.select_all(action='DESELECT')
        for obj in baked:
            obj.select_set(True)
        if part == 'Body':
            rig.select_set(True)
        bpy.context.view_layer.objects.active = baked[0]
        filepath = os.path.join(output, f'Chambara{color}{part}.fbx')
        bpy.ops.export_scene.fbx(filepath=filepath, use_selection=True, object_types={'MESH', 'ARMATURE'} if part == 'Body' else {'MESH'},
            apply_unit_scale=True, global_scale=1.0, axis_forward='-Z', axis_up='Y',
            bake_space_transform=part == 'Sword', use_mesh_modifiers=False, bake_anim=False,
            add_leaf_bones=False, path_mode='STRIP', mesh_smooth_type='FACE')
        for obj in baked:
            mesh = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            bpy.data.meshes.remove(mesh)
        print('EXPORTED', filepath)
    bpy.data.objects.remove(rig, do_unlink=True)
    bpy.data.armatures.remove(rig_data)
with open(os.path.join(output, 'ChambaraExport.json'), 'w') as file:
    json.dump(manifest, file, indent=2)
print('CHARACTER ANCHORS', manifest['characters'])
