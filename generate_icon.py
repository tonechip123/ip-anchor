from PIL import Image, ImageDraw, ImageFont
import os

# 创建多个尺寸的图标
sizes = [16, 32, 48, 64, 128, 256]
images = []

for size in sizes:
    # 创建透明背景
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # 绘制蓝色圆形背景
    margin = size * 0.1
    circle_bbox = [margin, margin, size - margin, size - margin]
    draw.ellipse(circle_bbox, fill=(30, 144, 255, 255), outline=(0, 0, 0, 255), width=max(1, size // 32))

    # 绘制白色 "IP" 文字
    try:
        font_size = int(size * 0.35)
        font = ImageFont.truetype("arial.ttf", font_size)
    except:
        font = ImageFont.load_default()

    text = "IP"
    # 获取文字边界框
    bbox = draw.textbbox((0, 0), text, font=font)
    text_width = bbox[2] - bbox[0]
    text_height = bbox[3] - bbox[1]

    # 居中绘制文字
    x = (size - text_width) / 2 - bbox[0]
    y = (size - text_height) / 2 - bbox[1]
    draw.text((x, y), text, fill=(255, 255, 255, 255), font=font)

    images.append(img)

# 保存为 ICO 文件
output_path = r'E:\openclaw\自动化脚本\IpMonitor\IpMonitor\app.ico'
images[0].save(output_path, format='ICO', sizes=[(img.width, img.height) for img in images], append_images=images[1:])

print(f"图标已生成: {output_path}")
