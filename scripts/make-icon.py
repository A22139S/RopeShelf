"""Generate RopeShelf's original peg-and-paper icon. Optional development tool: Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw

image = Image.new('RGBA', (256, 256))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((8, 8, 248, 248), radius=56, fill='#247B79')
draw.line([(20, 63), (55, 72), (105, 78), (155, 78), (205, 72), (236, 63)], fill='#E5CFAC', width=7, joint='curve')
draw.rounded_rectangle((53, 91, 204, 217), radius=16, fill='#D9ECEC', outline='#F5F7F8', width=3)
draw.rounded_rectangle((66, 106, 191, 189), radius=7, fill='#8BBDB9')
draw.ellipse((137, 119, 172, 154), fill='#F4DFB5')
draw.polygon([(66, 184), (107, 135), (146, 184)], fill='#356D68')
draw.line((112, 202, 145, 202), fill='#7CA19D', width=4)
draw.rounded_rectangle((119, 64, 137, 114), radius=5, fill='#E5CFAC', outline='#B68A55', width=2)
target = Path(__file__).resolve().parents[1] / 'src/RopeShelf.App/Assets/RopeShelf.ico'
target.parent.mkdir(parents=True, exist_ok=True)
image.save(target, format='ICO', sizes=[(16,16), (24,24), (32,32), (48,48), (64,64), (128,128), (256,256)])
print(target)
