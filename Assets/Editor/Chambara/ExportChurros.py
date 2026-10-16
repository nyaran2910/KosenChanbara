"""Blender 5.2 export for the Churroman body and the held churro weapon."""
import bpy
import json
import math
import os
import sys
from mathutils import Matrix, Vector

arguments = sys.argv[sys.argv.index("--") + 1:]
weapon_only = "--weapon-only" in arguments
arguments = [argument for argument in arguments if argument != "--weapon-only"]
character_source, weapon_source, output = arguments
os.makedirs(output, exist_ok=True)
UNITY_FORWARD = Matrix.Rotation(math.pi, 3, "Z")


def evaluated_vertices(obj, depsgraph):
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    points = [evaluated.matrix_world @ vertex.co for vertex in mesh.vertices]
    evaluated.to_mesh_clear()
    return points


def center(points):
    return Vector([(min(point[i] for point in points) + max(point[i] for point in points)) * 0.5
                   for i in range(3)])


def srgb(value):
    return value * 12.92 if value <= 0.0031308 else 1.055 * value ** (1 / 2.4) - 0.055


def material_data(material):
    node = next((n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) \
        if material.node_tree else None
    color = list(node.inputs["Base Color"].default_value) if node else list(material.diffuse_color)
    return {"name": material.name,
            "color": [srgb(value) for value in color[:3]] + [color[3]],
            "roughness": node.inputs["Roughness"].default_value if node else 0.6,
            "metallic": node.inputs["Metallic"].default_value if node else 0.0}


def export_body():
    bpy.ops.wm.open_mainfile(filepath=os.path.abspath(character_source))
    bpy.context.scene.frame_set(30)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    collection = bpy.data.collections["01_CHURROMAN_RED"]
    prefix = "CHURROMAN-RED_"
    source_rig = bpy.data.objects[prefix + "Rig"]
    body_objects = [obj for obj in collection.objects if obj.type == "MESH"
                    and not obj.name.startswith((prefix + "Glove_", prefix + "Glove_thumb_",
                                                  prefix + "Churro_body", prefix + "Churro_sugar"))]
    body_points = [point for obj in body_objects for point in evaluated_vertices(obj, depsgraph)]
    floor = min(point.z for point in body_points)
    scale = 2.0 / (max(point.z for point in body_points) - floor)
    character = bpy.data.objects[prefix + "Character"]
    body_origin = Vector((character.matrix_world.translation.x,
                          character.matrix_world.translation.y, floor))

    rig_data = bpy.data.armatures.new("ChurromanRestSkeleton")
    rig = bpy.data.objects.new("ChurromanRig", rig_data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for bone in source_rig.pose.bones:
        if bone.name == "shadow_model":
            continue
        rest = rig_data.edit_bones.new(bone.name)
        world = source_rig.matrix_world @ bone.matrix
        head = UNITY_FORWARD @ ((world.translation - body_origin) * scale)
        rotation = (UNITY_FORWARD @ world.to_3x3()).normalized().to_quaternion()
        rest.matrix = Matrix.LocRotScale(head, rotation, Vector((1, 1, 1)))
        rest.length = max(0.01, bone.length * source_rig.matrix_world.to_scale().x * scale)
    for bone in source_rig.pose.bones:
        if bone.name in rig_data.edit_bones and bone.parent and bone.parent.name in rig_data.edit_bones:
            rig_data.edit_bones[bone.name].parent = rig_data.edit_bones[bone.parent.name]
    bpy.ops.object.mode_set(mode="OBJECT")

    baked = []
    for obj in body_objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
        mesh.materials.clear()
        for slot in obj.material_slots:
            mesh.materials.append(slot.material)
        for vertex in mesh.vertices:
            world = evaluated.matrix_world @ vertex.co
            vertex.co = UNITY_FORWARD @ ((world - body_origin) * scale)
        mesh.update()
        result = bpy.data.objects.new("Baked_" + obj.name, mesh)
        bpy.context.scene.collection.objects.link(result)
        for group in obj.vertex_groups:
            if group.name != "shadow_model":
                result.vertex_groups.new(name=group.name)
        modifier = result.modifiers.new("Rest skin", "ARMATURE")
        modifier.object = rig
        result.parent = rig
        baked.append(result)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in baked:
        obj.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = baked[0]
    path = os.path.join(output, "ChurromanBody.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "ARMATURE"},
        apply_unit_scale=True, global_scale=1.0, axis_forward="-Z", axis_up="Y",
        bake_space_transform=False, use_mesh_modifiers=False, bake_anim=False,
        add_leaf_bones=False, path_mode="STRIP", mesh_smooth_type="FACE")
    print("EXPORTED BODY", path, "bounds height", max(point.z for point in body_points) - floor,
          "scale", scale, "bones", [bone.name for bone in source_rig.data.bones if bone.name != "shadow_model"])
    materials = {}
    # Retain all six armor colors for the editor's palette prefabs, including
    # colors not used by the red source collection selected for the mesh.
    for material in bpy.data.materials:
        if not material.name.startswith(("Studio", "Display")):
            materials[material.name] = material_data(material)
    for obj in baked:
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)
    bpy.data.objects.remove(rig, do_unlink=True)
    bpy.data.armatures.remove(rig_data)
    return materials


def export_weapon():
    bpy.ops.wm.open_mainfile(filepath=os.path.abspath(weapon_source))
    bpy.context.scene.frame_set(30)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    collection = bpy.data.collections["01_ORIGINAL_RED"]
    weapon_objects = [bpy.data.objects["ORIGINAL-RED_Churro_body"],
                      bpy.data.objects["ORIGINAL-RED_Churro_sugar"]]
    points = [point for obj in weapon_objects for point in evaluated_vertices(obj, depsgraph)]
    # The source churro is tilted in the character's hand. Recover its actual
    # centerline, then put the hilt centroid at the origin and align the line
    # with the weapon's positive local axis before the FBX axis conversion.
    import numpy as np
    point_array = np.asarray([tuple(point) for point in points], dtype=float)
    covariance = np.cov(point_array.T)
    _, eigenvectors = np.linalg.eigh(covariance)
    axis_array = eigenvectors[:, -1]
    if axis_array[2] < 0:
        axis_array = -axis_array
    center_array = point_array.mean(axis=0)
    projections = (point_array - center_array) @ axis_array
    low = point_array[projections <= np.quantile(projections, 0.02)].mean(axis=0)
    high = point_array[projections >= np.quantile(projections, 0.98)].mean(axis=0)
    axis_array = high - low
    axis_array /= np.linalg.norm(axis_array)
    if axis_array[2] < 0:
        axis_array = -axis_array
    axis = Vector(axis_array.tolist())
    # The source's right wrist marks the grasp plane. Center the churro's
    # actual cross-section at that plane, so the hilt origin sits inside the
    # runtime grip gloves while the rounded pastry continues below the hilt.
    source_rig = bpy.data.objects["ORIGINAL-RED_Rig"]
    wrist = source_rig.matrix_world @ source_rig.pose.bones["wrist_r"].matrix.translation
    wrist_projection = float(np.dot(np.asarray(tuple(wrist)) - center_array, axis_array))
    grip_points = point_array[np.abs(projections - wrist_projection) <= 0.04]
    grip = Vector(grip_points.mean(axis=0).tolist())
    scale = 2.1 / max((point - grip).dot(axis) for point in points)
    rotation = axis.rotation_difference(Vector((0, -1, 0))).to_matrix()
    baked = []
    materials = {}
    for obj in weapon_objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
        mesh.materials.clear()
        for slot in obj.material_slots:
            mesh.materials.append(slot.material)
            if slot.material:
                materials[slot.material.name] = material_data(slot.material)
        for vertex in mesh.vertices:
            world = evaluated.matrix_world @ vertex.co
            vertex.co = UNITY_FORWARD @ (rotation @ ((world - grip) * scale))
        mesh.update()
        result = bpy.data.objects.new("Baked_" + obj.name, mesh)
        bpy.context.scene.collection.objects.link(result)
        baked.append(result)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in baked:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = baked[0]
    path = os.path.join(output, "ChurrosBlade.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, global_scale=1.0, axis_forward="-Z", axis_up="Y",
        bake_space_transform=True, bake_anim=False, add_leaf_bones=False, path_mode="STRIP",
        mesh_smooth_type="FACE")
    transverse_centers = []
    longitudinal = (point_array - np.asarray(tuple(grip))) @ axis_array
    for low in np.linspace(longitudinal.min(), longitudinal.max(), 12)[:-1]:
        section = point_array[(longitudinal >= low) & (longitudinal < low + (longitudinal.max() - longitudinal.min()) / 11)]
        if len(section):
            section_center = section.mean(axis=0) - np.asarray(tuple(grip))
            transverse_centers.append(float(np.linalg.norm(section_center - axis_array * np.dot(section_center, axis_array))) * scale)
    print("EXPORTED WEAPON", path, "grip", tuple(grip), "axis", tuple(axis), "scale", scale,
          "pastry below grip", float(longitudinal.min() * scale), "max centerline drift", max(transverse_centers), "input bounds",
          tuple(min(point[i] for point in points) for i in range(3)),
          tuple(max(point[i] for point in points) for i in range(3)))
    for obj in baked:
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)
    return materials


body_materials = {} if weapon_only else export_body()
weapon_materials = export_weapon()
body_materials.update(weapon_materials)
if not weapon_only:
    with open(os.path.join(output, "ChurrosExport.json"), "w", encoding="utf-8") as output_file:
        json.dump({"materials": list(body_materials.values())}, output_file, indent=2)
    print("MATERIALS", sorted(body_materials))
