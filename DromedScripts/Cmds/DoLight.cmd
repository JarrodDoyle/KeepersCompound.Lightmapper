; Light the mission with KCTools and restore camera position (but not rotation unfortunately)
cube
new_brush
brush_to_cam 1
brush_set_time 0
force_wr_up2date

eval world set old_world %s
eval world mprint %s
save_cow KCLight.cow
run_sys_wait "Tools\KCTools\run_sys_wait_light.cmd"
load_file KCLight.cow
eval old_world set world %s

brush_select 1
cam_to_brush
delete_brush
force_wr_up2date
play_schema dinner_bell
