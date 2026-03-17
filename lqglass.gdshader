shader_type canvas_item;
// shader by Pcniado
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

uniform float u_roundness : hint_range(0.0, 1.0) = 1.0;

uniform float u_distortion_intensity = 1.0;
uniform float u_blur_intensity = 1.2;
uniform float u_opacity : hint_range(0.0, 1.0) = 0.7;
uniform float u_tint_strength : hint_range(0.0, 1.0) = 0.15;
uniform vec3 u_tint_color : source_color = vec3(0.82, 0.88, 0.94);

float sdf_box(vec2 p, vec2 b, float r) {
    vec2 d = abs(p) - b + vec2(r);
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - r;
}

vec3 get_blurred_color(sampler2D tex, vec2 screen_uv, vec2 pixel_size, float blur_radius) {
    vec3 color = vec3(0.0);
    float total_weight = 0.0;

    for (int x = -2; x <= 2; x++) {
        for (int y = -2; y <= 2; y++) {
            vec2 offset = vec2(float(x), float(y)) * blur_radius * pixel_size;
            float weight = exp(-0.5 * (float(x*x + y*y)) / 2.0);

            color += texture(tex, screen_uv + offset).rgb * weight;
            total_weight += weight;
        }
    }
    return color / total_weight;
}

void fragment() {
    vec2 pixel_scale = fwidth(UV);
    vec2 rect_size = 1.0 / pixel_scale;

    vec2 local_pos = (UV - 0.5) * rect_size;
    vec2 half_size = rect_size * 0.5;

    float max_radius = min(rect_size.x, rect_size.y) * 0.5;
    float current_radius = max_radius * u_roundness;

    float dist = sdf_box(local_pos, half_size, current_radius);

    if (dist > 0.0) {
        discard;
    }

    float min_side = min(rect_size.x, rect_size.y);
    float inversed_sdf = -dist / min_side;

    vec2 normalized_glass_coord = normalize(local_pos);
    float dist_from_center = 1.0 - clamp(inversed_sdf / 0.3, 0.0, 1.0);

    float distortion = 1.0 - sqrt(1.0 - pow(dist_from_center, 2.0));

    vec2 offset_pixels = distortion * normalized_glass_coord * half_size * u_distortion_intensity;
    vec2 screen_uv_offset = offset_pixels * SCREEN_PIXEL_SIZE;
    vec2 target_uv = SCREEN_UV - screen_uv_offset;
    float current_blur_radius = u_blur_intensity * (1.0 - dist_from_center * 0.5);

    float edge_factor = smoothstep(0.0, 0.02, inversed_sdf);
    vec2 chromatic_shift = normalized_glass_coord * edge_factor * 3.0 * SCREEN_PIXEL_SIZE;

    float r = get_blurred_color(screen_texture, target_uv - chromatic_shift, SCREEN_PIXEL_SIZE, current_blur_radius).r;
    float g = get_blurred_color(screen_texture, target_uv, SCREEN_PIXEL_SIZE, current_blur_radius).g;
    float b = get_blurred_color(screen_texture, target_uv + chromatic_shift, SCREEN_PIXEL_SIZE, current_blur_radius).b;

    vec3 glass_color = vec3(r, g, b);
    glass_color *= 0.9;
    glass_color = mix(glass_color, u_tint_color, u_tint_strength);

    // Keep the panel translucent so the frosted effect reads as glass.
    float alpha = clamp(edge_factor * u_opacity, 0.0, 1.0);
    COLOR = vec4(glass_color, alpha);
}
