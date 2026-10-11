"""Blender: --background --factory-startup --disable-autoexec --python this.py -- SOURCE.blend OUTPUT.fbx"""
import bpy
import bmesh
import os
import sys
from collections import Counter
from mathutils import Vector

source, output = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(source))
ob = next(o for o in bpy.data.objects if o.type == 'MESH')
points = [ob.matrix_world @ v.co for v in ob.data.vertices]
center = Vector(((min(p.x for p in points) + max(p.x for p in points)) / 2,
                 (min(p.y for p in points) + max(p.y for p in points)) / 2, 0))
heights = Counter(round(p.z, 3) for p in points if abs(p.x - center.x) < 10 and abs(p.y - center.y) < 10)
# The arena slab has equally dense bottom/top layers; play on its upper face.
peak = heights.most_common(1)[0][1]
center.z = max(height for height, count in heights.items() if count >= peak * 0.9)
print('ARENA CENTER / FLOOR', list(center), heights.most_common(6))
# Keep the atlas UVs; weld coincident geometry without dissolving tile borders.
for vertex, point in zip(ob.data.vertices, points):
    vertex.co = (point - center) * 0.25
ob.matrix_world.identity()
bm = bmesh.new()
bm.from_mesh(ob.data)
bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.00001)
bm.to_mesh(ob.data)
bm.free()
ob.data.update()
print('STAGE VERTICES / FACES', len(ob.data.vertices), len(ob.data.polygons))
bpy.ops.object.select_all(action='DESELECT')
ob.select_set(True)
bpy.context.view_layer.objects.active = ob
os.makedirs(os.path.dirname(os.path.abspath(output)), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=os.path.abspath(output), use_selection=True, object_types={'MESH'},
    apply_unit_scale=True, global_scale=1.0, axis_forward='-Z', axis_up='Y', bake_space_transform=True,
    bake_anim=False, path_mode='STRIP', mesh_smooth_type='FACE')
print('EXPORTED', output)
