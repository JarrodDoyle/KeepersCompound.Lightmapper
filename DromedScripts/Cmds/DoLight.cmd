; Light the mission with KCTools and restore camera position
; InvBeingTaken prop is used as a hacky way to grab the marker on load as it's never used in Thief FMs
cam_marker
hilight_clear
hilight_brush
hilight_add_prop InvBeingTaken

eval world set old_world %s
save_cow KCLight.cow
run_sys_wait "Tools\KCTools\run_sys_wait_light.cmd"
load_file KCLight.cow
eval old_world set world %s

hilight_clear
hilight_by_prop_direct InvBeingTaken
multibrush_the_highlight
cam_to_brush
delete_brush
force_wr_up2date
play_schema dinner_bell
